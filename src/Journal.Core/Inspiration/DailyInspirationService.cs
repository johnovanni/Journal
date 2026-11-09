using Journal.Core.Domain;

namespace Journal.Core.Inspiration;

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

    /// <summary>The Bible's books, in order.</summary>
    public IReadOnlyList<string> AvailableBooks() =>
        Verses(SourceNames.BuiltIn).Where(v => v.TraditionName() == VerseTraditions.Christianity).Select(v => v.Book).Distinct().ToList();

    /// <summary>The built-in traditions, Christianity first and then alphabetically, each with its books (FR-INSP-20).</summary>
    public IReadOnlyList<VerseTradition> AvailableVerseTraditions() =>
        Verses(SourceNames.BuiltIn)
            .GroupBy(v => v.TraditionName(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new VerseTradition(g.Key,
                g.Select(v => v.Scripture ?? VerseTraditions.Bible).Distinct().ToList(),
                g.Select(v => v.Book).Distinct().ToList()))
            .OrderBy(t => t.Name.Equals(VerseTraditions.Christianity, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

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
            verse = ChooseVerse(ByTradition(FilteredVerses(s, warnings)), s.Mode, strategy, date, vaultId);
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
            // Pick a tradition evenly, then a passage, so a long scripture doesn't crowd out the others.
            var groups = ByTradition(FilteredVerses(s, warnings));
            if (groups.Count == 1) verse = PickOther(groups[0], current.Verse, random);
            else if (groups.Count > 1)
            {
                do
                {
                    var group = groups[random.Next(groups.Count)];
                    verse = group[random.Next(group.Count)];
                }
                while (Equals(verse, current.Verse));
            }
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

    /// <summary>
    /// The day's verse (FR-INSP-20): with one tradition, exactly as before ISS-012. With several, a tradition is
    /// chosen evenly first, then a passage within it. By day of year, the days cycle through the traditions and
    /// each tradition's passages in turn, so the same date still gets the same verse every year.
    /// </summary>
    private static Verse? ChooseVerse(IReadOnlyList<IReadOnlyList<Verse>> groups, SelectionMode mode, ISelectionStrategy strategy, DateOnly date, Guid vaultId)
    {
        if (groups.Count == 0) return null;
        if (groups.Count == 1)
        {
            var i = strategy.SelectIndex(groups[0].Count, date, vaultId, "verse");
            return i >= 0 ? groups[0][i] : null;
        }
        if (mode == SelectionMode.DayOfYear)
        {
            var day = date.DayOfYear - 1;
            var g = groups[day % groups.Count];
            return g[day / groups.Count % g.Count];
        }
        var group = groups[strategy.SelectIndex(groups.Count, date, vaultId, "verse-tradition")];
        return group[strategy.SelectIndex(group.Count, date, vaultId, "verse")];
    }

    /// <summary>The verses grouped by tradition, Christianity first and then alphabetically.</summary>
    private static IReadOnlyList<IReadOnlyList<Verse>> ByTradition(IReadOnlyList<Verse> verses) =>
        verses.GroupBy(v => v.TraditionName(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key.Equals(VerseTraditions.Christianity, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (IReadOnlyList<Verse>)g.ToList())
            .ToList();

    private IReadOnlyList<Verse> FilteredVerses(InspirationSettings s, List<string> warnings)
    {
        var all = Verses(s.VerseSource);
        if (all.Count == 0 && s.VerseSource != SourceNames.BuiltIn)
        {
            warnings.Add("The imported verse list is empty, so the built-in verses are used.");
            all = Verses(SourceNames.BuiltIn);
        }
        else if (s.VerseSource == SourceNames.BuiltIn)
        {
            var shown = all.Where(v => s.VerseTraditions.Contains(v.TraditionName(), StringComparer.OrdinalIgnoreCase)).ToList();
            if (shown.Count > 0) all = shown;
            else warnings.Add("No verse traditions are ticked, so all verses are used.");
        }
        if (s.VerseBooks.Count == 0) return all;
        var filtered = FilterByBook(all, s.VerseBooks);
        if (filtered.Count > 0) return filtered;
        warnings.Add("No verses match the chosen books, so all verses are used.");
        return all;
    }

    /// <summary>
    /// Applies the book choices per tradition (FR-INSP-20): a tradition with no ticked book keeps all its verses.
    /// Choices are "Tradition/Book"; a plain book name (as saved before ISS-012) is a Bible book, and also
    /// applies to imported verses, which have no tradition.
    /// </summary>
    private static List<Verse> FilterByBook(IReadOnlyList<Verse> all, IReadOnlySet<string> choices)
    {
        var ticked = new HashSet<(string, string)>(StringTupleComparer.Instance);
        foreach (var c in choices)
        {
            if (TraditionChoice.TryParse(c, out var tradition, out var book)) ticked.Add((tradition, book));
            else
            {
                ticked.Add((VerseTraditions.Christianity, c));
                ticked.Add(("", c));
            }
        }
        var filteredTraditions = ticked.Select(t => t.Item1).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(v => !filteredTraditions.Contains(v.TraditionName()) || ticked.Contains((v.TraditionName(), v.Book))).ToList();
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
