using Journal.Core.Documents;
using Journal.Core.Domain;

namespace Journal.Core.Persistence;

/// <summary>Searchable summary of one tab, stored in the encrypted catalog.</summary>
public sealed record CatalogTab(
    Guid Id,
    string Title,
    string? Tag,
    string BodyText,
    IReadOnlyList<string> ImageCaptions,
    IReadOnlyList<string> AttachmentText);

/// <summary>Summary of one entry used for the calendar and search without decrypting every entry.</summary>
public sealed record CatalogEntry(
    DateOnly Date,
    IReadOnlyList<string> Tags,
    IReadOnlyList<CatalogTab> Tabs,
    int OpenChecklistItems,
    int DoneChecklistItems,
    IReadOnlyList<Guid> BlobIds)
{
    public static CatalogEntry From(JournalEntry e)
    {
        int open = 0, done = 0;
        var tabs = new List<CatalogTab>();
        foreach (var t in e.Tabs)
        {
            var (o, d) = t.Document.ChecklistCounts();
            open += o;
            done += d;
            tabs.Add(new CatalogTab(
                t.Id, t.Title, t.Tag?.Name,
                t.Document.ExtractPlainText(),
                t.Document.Images().Select(i => i.Caption).Where(c => c.Length > 0).ToList(),
                t.Document.Attachments().Select(a => a.Description.Length > 0 ? $"{a.FileName} {a.Description}" : a.FileName).ToList()));
        }
        return new CatalogEntry(e.Date, e.AllTags().Select(t => t.Name).ToList(), tabs, open, done,
            e.ReferencedBlobs().Select(b => b.Id).Distinct().ToList());
    }
}

/// <summary>All catalog entries keyed by date.</summary>
public sealed class Catalog
{
    public const int FormatVersion = 1;

    private readonly SortedDictionary<DateOnly, CatalogEntry> _entries = [];

    public Catalog() { }

    public Catalog(IEnumerable<CatalogEntry> entries)
    {
        foreach (var e in entries) _entries[e.Date] = e;
    }

    public IReadOnlyCollection<CatalogEntry> Entries => _entries.Values;
    public int Count => _entries.Count;

    public CatalogEntry? Get(DateOnly date) => _entries.GetValueOrDefault(date);

    public bool Contains(DateOnly date) => _entries.ContainsKey(date);

    public void Upsert(CatalogEntry entry) => _entries[entry.Date] = entry;

    public bool Remove(DateOnly date) => _entries.Remove(date);

    public ISet<DateOnly> DatesInRange(DateOnly from, DateOnly to) =>
        _entries.Keys.Where(d => d >= from && d <= to).ToHashSet();

    public ISet<DateOnly> DatesWithOpenItems(DateOnly from, DateOnly to) =>
        _entries.Values.Where(e => e.Date >= from && e.Date <= to && e.OpenChecklistItems > 0).Select(e => e.Date).ToHashSet();

    /// <summary>Distinct tags in display case (first spelling seen), sorted.</summary>
    public IReadOnlyList<string> AllTags() =>
        _entries.Values.SelectMany(e => e.Tags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public ISet<Guid> ReferencedBlobIds() => _entries.Values.SelectMany(e => e.BlobIds).ToHashSet();
}
