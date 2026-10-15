using Journal.Core.Persistence;

namespace Journal.Core.Search;

public readonly record struct HighlightRange(int Start, int Length);

public sealed record SearchHit(
    DateOnly Date,
    Guid? TabId,
    string TabTitle,
    MatchSource Source,
    string Snippet,
    IReadOnlyList<HighlightRange> Highlights);

public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, IReadOnlyList<string> Warnings, bool Truncated = false)
{
    public static readonly SearchResult Empty = new([], []);
}

public interface ISearchIndex
{
    void Rebuild(Catalog catalog);
    void Upsert(CatalogEntry entry);
    void Remove(DateOnly date);
    /// <summary>Returns at most <paramref name="maxHits"/> hits, newest first.</summary>
    IReadOnlyList<SearchHit> Query(SearchQuery query, int maxHits = int.MaxValue);
    int Count { get; }
}

/// <summary>Builds a short excerpt around the first match with the matched words marked.</summary>
public static class SnippetBuilder
{
    public const int MaxLength = 160;

    public static (string Snippet, IReadOnlyList<HighlightRange> Highlights) Build(
        string text, IReadOnlyCollection<string> terms, IReadOnlyCollection<string> phrases, int maxLength = MaxLength)
    {
        text = text.Replace('\n', ' ');
        var folded = TextFolding.Fold(text);
        var matches = FindMatches(folded, terms, phrases).OrderBy(m => m.Start).ToList();

        int start = 0;
        if (text.Length > maxLength && matches.Count > 0)
        {
            int first = matches[0].Start;
            start = Math.Max(0, first - (maxLength / 3));
            if (start > 0)
            {
                // Begin at a word boundary.
                int space = text.IndexOf(' ', start);
                if (space >= 0 && space < first) start = space + 1;
            }
        }
        int prefix = start > 0 ? 1 : 0;
        int available = maxLength - prefix;
        int length = Math.Min(text.Length - start, available);
        bool truncatedEnd = start + length < text.Length;
        if (truncatedEnd)
        {
            length = Math.Max(1, length - 1);
            int lastSpace = text.LastIndexOf(' ', start + length - 1, length);
            if (lastSpace > start + length / 2) length = lastSpace - start;
        }
        var body = text.Substring(start, length);
        var snippet = (prefix == 1 ? "…" : "") + body + (truncatedEnd ? "…" : "");

        var highlights = matches
            .Where(m => m.Start >= start && m.Start + m.Length <= start + length)
            .Select(m => new HighlightRange(m.Start - start + prefix, m.Length))
            .ToList();
        return (snippet, highlights);
    }

    internal static IEnumerable<HighlightRange> FindMatches(string folded, IEnumerable<string> terms, IEnumerable<string> phrases)
    {
        foreach (var term in terms.Where(t => t.Length > 0))
        {
            int i = 0;
            while ((i = folded.IndexOf(term, i, StringComparison.Ordinal)) >= 0)
            {
                bool wordStart = i == 0 || !TextFolding.IsWordChar(folded[i - 1]);
                if (wordStart)
                {
                    int end = i + term.Length;
                    while (end < folded.Length && TextFolding.IsWordChar(folded[end])) end++;
                    yield return new HighlightRange(i, end - i);
                }
                i += term.Length;
            }
        }
        foreach (var phrase in phrases.Where(p => p.Length > 0))
        {
            // Phrases are matched on normalized words; locate the first word sequence in the original text.
            var words = phrase.Split(' ');
            int i = 0;
            while ((i = folded.IndexOf(words[0], i, StringComparison.Ordinal)) >= 0)
            {
                int pos = i, end = -1;
                bool ok = true;
                foreach (var w in words)
                {
                    while (pos < folded.Length && !TextFolding.IsWordChar(folded[pos])) pos++;
                    if (string.CompareOrdinal(folded, pos, w, 0, w.Length) != 0 ||
                        (pos + w.Length < folded.Length && TextFolding.IsWordChar(folded[pos + w.Length]))) { ok = false; break; }
                    pos += w.Length;
                    end = pos;
                }
                if (ok && (i == 0 || !TextFolding.IsWordChar(folded[i - 1])))
                {
                    yield return new HighlightRange(i, end - i);
                    break;
                }
                i++;
            }
        }
    }
}

/// <summary>
/// In-memory index over the decrypted catalog (D8). Each tab is a search unit; all terms must
/// match within one tab (its text, captions, files) or the entry's tags (FR-SRCH-04).
/// </summary>
public sealed class InMemorySearchIndex : ISearchIndex
{
    private readonly Dictionary<DateOnly, IndexedEntry> _entries = [];

    public int Count => _entries.Count;

    public void Rebuild(Catalog catalog)
    {
        _entries.Clear();
        foreach (var e in catalog.Entries) Upsert(e);
    }

    public void Upsert(CatalogEntry entry) => _entries[entry.Date] = new IndexedEntry(entry);

    public void Remove(DateOnly date) => _entries.Remove(date);

    public IReadOnlyList<SearchHit> Query(SearchQuery q, int maxHits = int.MaxValue)
    {
        if (q.IsEmpty) return [];
        var hits = new List<SearchHit>();
        var tagFilter = q.Tags.Select(t => t.Trim().ToLowerInvariant()).ToList();

        foreach (var e in _entries.Values.OrderByDescending(e => e.Entry.Date))
        {
            if (hits.Count >= maxHits) break;
            var c = e.Entry;
            if (q.From is { } from && c.Date < from) continue;
            if (q.To is { } to && c.Date > to) continue;
            if (q.Checklist == ChecklistFilter.Open && c.OpenChecklistItems == 0) continue;
            if (q.Checklist == ChecklistFilter.Done && c.DoneChecklistItems == 0) continue;
            if (!tagFilter.All(t => e.TagSet.Contains(t))) continue;

            if (!q.HasTextCriteria)
            {
                hits.Add(WholeEntryHit(e, tagFilter));
                continue;
            }

            foreach (var unit in e.Units)
            {
                var source = Match(unit, e, q);
                if (source is null) continue;
                var text = SourceText(unit, e, source.Value, q);
                var (snippet, hl) = SnippetBuilder.Build(text, q.Terms, q.Phrases);
                hits.Add(new SearchHit(c.Date, unit.Tab.Id, unit.Tab.Title, source.Value, snippet, hl));
            }
        }
        return hits;
    }

    private static SearchHit WholeEntryHit(IndexedEntry e, List<string> tagFilter)
    {
        // Prefer the tab whose tag was searched for.
        var unit = e.Units.FirstOrDefault(u => u.Tab.Tag is { } t && tagFilter.Contains(t.ToLowerInvariant())) ?? e.Units[0];
        var (snippet, hl) = SnippetBuilder.Build(unit.Tab.BodyText.Length > 0 ? unit.Tab.BodyText : string.Join(", ", e.Entry.Tags), [], []);
        return new SearchHit(e.Entry.Date, unit.Tab.Id, unit.Tab.Title, tagFilter.Count > 0 ? MatchSource.Tag : MatchSource.Text, snippet, hl);
    }

    /// <summary>Returns where the first criterion matched, or null when some criterion is not satisfied.</summary>
    private static MatchSource? Match(IndexedUnit unit, IndexedEntry e, SearchQuery q)
    {
        MatchSource? first = null;
        foreach (var term in q.Terms)
        {
            var s = FindTerm(unit, e, term, q.Scope);
            if (s is null) return null;
            first ??= s;
        }
        foreach (var phrase in q.Phrases)
        {
            var s = FindPhrase(unit, e, phrase, q.Scope);
            if (s is null) return null;
            first ??= s;
        }
        return first;
    }

    private static MatchSource? FindTerm(IndexedUnit u, IndexedEntry e, string term, SearchScope scope)
    {
        if (scope.HasFlag(SearchScope.Text) && HasPrefix(u.BodyWords, term)) return MatchSource.Text;
        if (scope.HasFlag(SearchScope.Captions) && u.CaptionWords.Any(w => HasPrefix(w, term))) return MatchSource.ImageCaption;
        if (scope.HasFlag(SearchScope.Files) && u.FileWords.Any(w => HasPrefix(w, term))) return MatchSource.Attachment;
        if (scope.HasFlag(SearchScope.Tags) && HasPrefix(e.TagWords, term)) return MatchSource.Tag;
        return null;
    }

    private static MatchSource? FindPhrase(IndexedUnit u, IndexedEntry e, string phrase, SearchScope scope)
    {
        var p = " " + phrase + " ";
        if (scope.HasFlag(SearchScope.Text) && u.BodyWordString.Contains(p, StringComparison.Ordinal)) return MatchSource.Text;
        if (scope.HasFlag(SearchScope.Captions) && u.CaptionWordStrings.Any(c => c.Contains(p, StringComparison.Ordinal))) return MatchSource.ImageCaption;
        if (scope.HasFlag(SearchScope.Files) && u.FileWordStrings.Any(c => c.Contains(p, StringComparison.Ordinal))) return MatchSource.Attachment;
        if (scope.HasFlag(SearchScope.Tags) && e.TagWordString.Contains(p, StringComparison.Ordinal)) return MatchSource.Tag;
        return null;
    }

    private static string SourceText(IndexedUnit u, IndexedEntry e, MatchSource source, SearchQuery q) => source switch
    {
        MatchSource.ImageCaption => u.Tab.ImageCaptions.FirstOrDefault(c => Contains(c, q)) ?? u.Tab.ImageCaptions.FirstOrDefault() ?? "",
        MatchSource.Attachment => u.Tab.AttachmentText.FirstOrDefault(c => Contains(c, q)) ?? u.Tab.AttachmentText.FirstOrDefault() ?? "",
        MatchSource.Tag => u.Tab.BodyText.Length > 0 ? u.Tab.BodyText : string.Join(", ", e.Entry.Tags),
        _ => u.Tab.BodyText,
    };

    private static bool Contains(string text, SearchQuery q)
    {
        var words = TextFolding.Words(TextFolding.Fold(text)).ToList();
        return q.Terms.Any(t => words.Any(w => w.StartsWith(t, StringComparison.Ordinal))) ||
               q.Phrases.Any(p => TextFolding.WordString(text).Contains(" " + p + " ", StringComparison.Ordinal));
    }

    private static bool HasPrefix(string[] sortedWords, string term)
    {
        // Binary search for the first word >= term, then check its prefix.
        int lo = 0, hi = sortedWords.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (string.CompareOrdinal(sortedWords[mid], term) < 0) lo = mid + 1; else hi = mid;
        }
        return lo < sortedWords.Length && sortedWords[lo].StartsWith(term, StringComparison.Ordinal);
    }

    private static string[] SortedWords(string text) =>
        TextFolding.Words(TextFolding.Fold(text)).Where(w => w.Length > 0).Distinct().OrderBy(w => w, StringComparer.Ordinal).ToArray();

    private sealed class IndexedEntry
    {
        public CatalogEntry Entry { get; }
        public HashSet<string> TagSet { get; }
        public string[] TagWords { get; }
        public string TagWordString { get; }
        public List<IndexedUnit> Units { get; }

        public IndexedEntry(CatalogEntry entry)
        {
            Entry = entry;
            TagSet = entry.Tags.Select(t => t.ToLowerInvariant()).ToHashSet();
            var tagText = string.Join(" , ", entry.Tags);
            TagWords = SortedWords(tagText);
            TagWordString = TextFolding.WordString(tagText);
            Units = entry.Tabs.Select(t => new IndexedUnit(t)).ToList();
            if (Units.Count == 0) Units.Add(new IndexedUnit(new CatalogTab(Guid.Empty, "Journal", null, "", [], [])));
        }
    }

    private sealed class IndexedUnit(CatalogTab tab)
    {
        public CatalogTab Tab { get; } = tab;
        public string[] BodyWords { get; } = SortedWords(tab.BodyText);
        public string BodyWordString { get; } = TextFolding.WordString(tab.BodyText);
        public string[][] CaptionWords { get; } = tab.ImageCaptions.Select(SortedWords).ToArray();
        public string[] CaptionWordStrings { get; } = tab.ImageCaptions.Select(TextFolding.WordString).ToArray();
        public string[][] FileWords { get; } = tab.AttachmentText.Select(SortedWords).ToArray();
        public string[] FileWordStrings { get; } = tab.AttachmentText.Select(TextFolding.WordString).ToArray();
    }
}

/// <summary>Parses the query text and runs it against the index.</summary>
public sealed class SearchService(SearchQueryParser parser, ISearchIndex index)
{
    /// <summary>Results beyond this are not shown; the newest matches come first.</summary>
    public const int MaxResults = 200;

    public ISearchIndex Index => index;

    public SearchResult Search(string? text)
    {
        var q = parser.Parse(text);
        if (q.IsEmpty) return new SearchResult([], q.Warnings);
        var hits = index.Query(q, MaxResults + 1);
        bool truncated = hits.Count > MaxResults;
        return new SearchResult(truncated ? hits.Take(MaxResults).ToList() : hits, q.Warnings, truncated);
    }
}
