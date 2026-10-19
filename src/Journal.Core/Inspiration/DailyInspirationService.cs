using System.Security.Cryptography;
using System.Text;
using Journal.Core.Domain;

namespace Journal.Core.Inspiration;

public enum SelectionMode
{
    /// <summary>The same date gets the same item every year.</summary>
    DayOfYear,
    /// <summary>A random item per date, stable for that date and vault.</summary>
    RandomPerDay,
}

public static class SourceNames
{
    public const string BuiltIn = "builtin";
    public const string Imported = "imported";
}

public sealed record InspirationSettings
{
    public bool ShowVerse { get; init; } = true;
    public bool ShowQuote { get; init; } = true;
    public string VerseSource { get; init; } = SourceNames.BuiltIn;
    public IReadOnlySet<string> VerseBooks { get; init; } = new HashSet<string>();
    public string QuoteSource { get; init; } = SourceNames.BuiltIn;
    public IReadOnlySet<string> Traditions { get; init; } = new HashSet<string>();
    /// <summary>
    /// Quote categories the user turned off (FR-INSP-17). Stored as the ones turned off, so a category
    /// added to the content later is shown until the user turns it off.
    /// </summary>
    public IReadOnlySet<string> HiddenCategories { get; init; } = new HashSet<string>();
    public SelectionMode Mode { get; init; } = SelectionMode.DayOfYear;
}

public interface IVerseSource
{
    string Name { get; }
    IReadOnlyList<Verse> All();
}

public interface IQuoteSource
{
    string Name { get; }
    IReadOnlyList<Quote> All();
}

public interface ISelectionStrategy
{
    int SelectIndex(int count, DateOnly date, Guid vaultId, string salt);
}

public sealed class DayOfYearStrategy : ISelectionStrategy
{
    public int SelectIndex(int count, DateOnly date, Guid vaultId, string salt) =>
        count == 0 ? -1 : (date.DayOfYear - 1) % count;
}

public sealed class SeededRandomStrategy : ISelectionStrategy
{
    public int SelectIndex(int count, DateOnly date, Guid vaultId, string salt)
    {
        if (count == 0) return -1;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{vaultId:N}|{date:yyyy-MM-dd}|{salt}"));
        return (int)(BitConverter.ToUInt32(hash, 0) % (uint)count);
    }
}

/// <summary>A quote category and the traditions its quotes come from.</summary>
public sealed record QuoteCategory(string Name, IReadOnlyList<string> Traditions);

/// <summary>A ticked tradition saved with its category, e.g. "Philosophy/Chinese" (ISS-015).</summary>
public static class TraditionChoice
{
    public const char Separator = '/';

    public static string Make(string category, string tradition) => $"{category}{Separator}{tradition}";

    public static bool TryParse(string choice, out string category, out string tradition)
    {
        var i = choice.IndexOf(Separator);
        category = i > 0 ? choice[..i] : "";
        tradition = i > 0 ? choice[(i + 1)..] : choice;
        return i > 0 && tradition.Length > 0;
    }
}

public sealed record InspirationResult(DailyInspiration Inspiration, IReadOnlyList<string> Warnings);

/// <summary>Chooses the verse and quote for a day from the configured sources (FR-INSP-*).</summary>
public sealed class DailyInspirationService
{
    private readonly Dictionary<string, IVerseSource> _verseSources;
    private readonly Dictionary<string, IQuoteSource> _quoteSources;

    public DailyInspirationService(IEnumerable<IVerseSource> verseSources, IEnumerable<IQuoteSource> quoteSources)
    {
        _verseSources = verseSources.ToDictionary(s => s.Name);
        _quoteSources = quoteSources.ToDictionary(s => s.Name);
    }

    public IReadOnlyList<string> AvailableBooks() =>
        Verses(SourceNames.BuiltIn).Select(v => v.Book).Distinct().ToList();

    public IReadOnlyList<string> AvailableTraditions() =>
        Quotes(SourceNames.BuiltIn).Select(q => q.Tradition).Distinct().Order().ToList();

    /// <summary>The built-in quote categories, each with its traditions (FR-INSP-17).</summary>
    public IReadOnlyList<QuoteCategory> AvailableCategories() =>
        Quotes(SourceNames.BuiltIn)
            .GroupBy(q => q.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new QuoteCategory(g.Key, g.Select(q => q.Tradition).Distinct().Order().ToList()))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public InspirationResult For(DateOnly date, InspirationSettings s, Guid vaultId)
    {
        var warnings = new List<string>();
        var strategy = Strategy(s.Mode);
        Verse? verse = null;
        Quote? quote = null;
        if (s.ShowVerse)
        {
            var list = FilteredVerses(s, warnings);
            var i = strategy.SelectIndex(list.Count, date, vaultId, "verse");
            verse = i >= 0 ? list[i] : null;
        }
        if (s.ShowQuote)
        {
            var list = FilteredQuotes(s, warnings);
            var i = strategy.SelectIndex(list.Count, date, vaultId, "quote");
            quote = i >= 0 ? list[i] : null;
        }
        return new InspirationResult(new DailyInspiration(verse, quote), warnings);
    }

    /// <summary>Picks a different verse and quote than <paramref name="current"/> (FR-INSP-09).</summary>
    public DailyInspiration Shuffle(DailyInspiration current, InspirationSettings s, Random? random = null)
    {
        random ??= Random.Shared;
        var warnings = new List<string>();
        Verse? verse = null;
        Quote? quote = null;
        if (s.ShowVerse)
        {
            var list = FilteredVerses(s, warnings);
            verse = PickOther(list, current.Verse, random);
        }
        if (s.ShowQuote)
        {
            var list = FilteredQuotes(s, warnings);
            quote = PickOther(list, current.Quote, random);
        }
        return new DailyInspiration(verse, quote);
    }

    /// <summary>
    /// Replaces a stored verse that is only part of a sentence (saved before the built-in list was corrected)
    /// with the built-in passage that contains it. Other verses are returned unchanged.
    /// </summary>
    public Verse? CompletePassage(Verse? stored)
    {
        if (stored is null || IsCompleteSentence(stored.Text) || !TryParseRange(stored.Reference, out var book, out var ch, out var v1, out var v2))
            return stored;
        foreach (var candidate in Verses(SourceNames.BuiltIn))
        {
            if (candidate.Translation == stored.Translation &&
                TryParseRange(candidate.Reference, out var b, out var c, out var c1, out var c2) &&
                b == book && c == ch && c1 <= v1 && c2 >= v2)
                return candidate;
        }
        return stored;
    }

    public static bool IsCompleteSentence(string text)
    {
        var t = text.TrimEnd();
        var first = t.FirstOrDefault(char.IsLetter);
        return first != default && char.IsUpper(first) && t.Length > 0 && ".!?”’\")".Contains(t[^1]);
    }

    private static bool TryParseRange(string reference, out string book, out int chapter, out int from, out int to)
    {
        var m = System.Text.RegularExpressions.Regex.Match(reference, @"^(.+) (\d+):(\d+)(?:-(\d+))?$");
        book = m.Success ? m.Groups[1].Value : "";
        chapter = m.Success ? int.Parse(m.Groups[2].Value) : 0;
        from = m.Success ? int.Parse(m.Groups[3].Value) : 0;
        to = m.Success && m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : from;
        return m.Success;
    }

    /// <summary>Applies the show/hide settings to a stored inspiration without choosing new items.</summary>
    public static DailyInspiration ApplyVisibility(DailyInspiration stored, InspirationSettings s) =>
        new(s.ShowVerse ? stored.Verse : null, s.ShowQuote ? stored.Quote : null);

    private static T? PickOther<T>(IReadOnlyList<T> list, T? current, Random random) where T : class
    {
        if (list.Count == 0) return null;
        if (list.Count == 1) return list[0];
        T pick;
        do pick = list[random.Next(list.Count)];
        while (Equals(pick, current));
        return pick;
    }

    private IReadOnlyList<Verse> FilteredVerses(InspirationSettings s, List<string> warnings)
    {
        var all = Verses(s.VerseSource);
        if (all.Count == 0 && s.VerseSource != SourceNames.BuiltIn)
        {
            warnings.Add("The imported verse list is empty, so the built-in verses are used.");
            all = Verses(SourceNames.BuiltIn);
        }
        if (s.VerseBooks.Count == 0) return all;
        var filtered = all.Where(v => s.VerseBooks.Contains(v.Book, StringComparer.OrdinalIgnoreCase)).ToList();
        if (filtered.Count > 0) return filtered;
        warnings.Add("No verses match the chosen books, so all verses are used.");
        return all;
    }

    private IReadOnlyList<Quote> FilteredQuotes(InspirationSettings s, List<string> warnings)
    {
        var all = Quotes(s.QuoteSource);
        if (all.Count == 0 && s.QuoteSource != SourceNames.BuiltIn)
        {
            warnings.Add("The imported quote list is empty, so the built-in quotes are used.");
            all = Quotes(SourceNames.BuiltIn);
        }
        if (s.HiddenCategories.Count > 0)
        {
            var shown = all.Where(q => !s.HiddenCategories.Contains(q.Category, StringComparer.OrdinalIgnoreCase)).ToList();
            if (shown.Count > 0) all = shown;
            else warnings.Add("All quote categories are turned off, so all quotes are used.");
        }
        if (s.Traditions.Count == 0) return all;
        var filtered = FilterByTradition(all, s.Traditions);
        if (filtered.Count > 0) return filtered;
        warnings.Add("No quotes match the chosen traditions, so all quotes are used.");
        return all;
    }

    /// <summary>
    /// Applies the tradition choices per category (FR-INSP-06, ISS-015): a category with no ticked tradition
    /// keeps all its quotes; otherwise only its ticked traditions. Choices are "Category/Tradition"; a bare
    /// tradition (settings saved before ISS-015) applies to every category that has that tradition.
    /// </summary>
    private static List<Quote> FilterByTradition(IReadOnlyList<Quote> all, IReadOnlySet<string> choices)
    {
        var ticked = new HashSet<(string, string)>(StringTupleComparer.Instance);
        var bare = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in choices)
        {
            if (TraditionChoice.TryParse(c, out var category, out var tradition)) ticked.Add((category, tradition));
            else bare.Add(c);
        }
        foreach (var q in all)
            if (bare.Contains(q.Tradition)) ticked.Add((q.Category, q.Tradition));
        var filteredCategories = ticked.Select(t => t.Item1).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(q => !filteredCategories.Contains(q.Category) || ticked.Contains((q.Category, q.Tradition))).ToList();
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
    {
        public static readonly StringTupleComparer Instance = new();
        public bool Equals((string, string) x, (string, string) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Item1, y.Item1) && StringComparer.OrdinalIgnoreCase.Equals(x.Item2, y.Item2);
        public int GetHashCode((string, string) t) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(t.Item1), StringComparer.OrdinalIgnoreCase.GetHashCode(t.Item2));
    }

    private IReadOnlyList<Verse> Verses(string name) =>
        _verseSources.TryGetValue(name, out var src) ? src.All() : [];

    private IReadOnlyList<Quote> Quotes(string name) =>
        _quoteSources.TryGetValue(name, out var src) ? src.All() : [];

    private static ISelectionStrategy Strategy(SelectionMode mode) =>
        mode == SelectionMode.RandomPerDay ? new SeededRandomStrategy() : new DayOfYearStrategy();
}

/// <summary>A verse or quote source backed by an in-memory list (imported lists, tests).</summary>
public sealed class ListVerseSource(string name, Func<IReadOnlyList<Verse>> items) : IVerseSource
{
    public string Name => name;
    public IReadOnlyList<Verse> All() => items();
}

public sealed class ListQuoteSource(string name, Func<IReadOnlyList<Quote>> items) : IQuoteSource
{
    public string Name => name;
    public IReadOnlyList<Quote> All() => items();
}
