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
        if (s.Traditions.Count == 0) return all;
        var filtered = all.Where(q => s.Traditions.Contains(q.Tradition, StringComparer.OrdinalIgnoreCase)).ToList();
        if (filtered.Count > 0) return filtered;
        warnings.Add("No quotes match the chosen traditions, so all quotes are used.");
        return all;
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
