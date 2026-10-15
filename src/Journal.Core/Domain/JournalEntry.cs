using Journal.Core.Documents;

namespace Journal.Core.Domain;

public sealed record Verse(string Reference, string Text, string Translation)
{
    /// <summary>Book name, e.g. "Psalms" for "Psalm 23:1" (derived from the reference when not stored).</summary>
    public string Book { get; init; } = BookOf(Reference);

    public static string BookOf(string reference)
    {
        var space = reference.LastIndexOf(' ');
        var book = space > 0 ? reference[..space] : reference;
        return book == "Psalm" ? "Psalms" : book;
    }
}

public sealed record Quote(string Text, string Author, string? Work, string Tradition)
{
    public string? Source { get; init; }
}

/// <summary>The verse and quote shown on a day. Either may be absent when turned off.</summary>
public sealed record DailyInspiration(Verse? Verse, Quote? Quote)
{
    public static readonly DailyInspiration None = new(null, null);
}

public sealed class DuplicateTagException(Tag tag)
    : InvalidOperationException($"This entry already has a tab for the tag \"{tag.Name}\".")
{
    public Tag Tag { get; } = tag;
}

/// <summary>One tab of an entry. The first tab is untagged and titled "Journal".</summary>
public sealed class EntryTab
{
    public const string DefaultTitle = "Journal";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Tag? Tag { get; internal set; }
    public JournalDocument Document { get; set; } = new();

    public string Title => Tag?.Name ?? DefaultTitle;
    public bool IsDefault => Tag is null;

    public EntryTab() { }

    public EntryTab(Tag? tag, JournalDocument? document = null)
    {
        Tag = tag;
        Document = document ?? new JournalDocument();
    }
}

/// <summary>
/// A day's journal entry: one or more tabs, extra tags and the day's verse and quote.
/// Enforces: exactly one untagged tab, which stays first; no two tabs share a tag.
/// </summary>
public sealed class JournalEntry
{
    private readonly List<EntryTab> _tabs = [];
    private readonly HashSet<Tag> _extraTags = [];

    public DateOnly Date { get; }
    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset ModifiedUtc { get; private set; }
    public DailyInspiration Inspiration { get; set; } = DailyInspiration.None;
    /// <summary>True once the verse/quote has been stored with the entry (FR-INSP-10).</summary>
    public bool IsInspirationPinned { get; set; }

    public IReadOnlyList<EntryTab> Tabs => _tabs;
    public IReadOnlyCollection<Tag> ExtraTags => _extraTags;
    public EntryTab DefaultTab => _tabs[0];

    private JournalEntry(DateOnly date, DateTimeOffset createdUtc)
    {
        Date = date;
        CreatedUtc = createdUtc;
        ModifiedUtc = createdUtc;
    }

    public static JournalEntry Create(DateOnly date, DateTimeOffset nowUtc)
    {
        var entry = new JournalEntry(date, nowUtc);
        entry._tabs.Add(new EntryTab(null));
        return entry;
    }

    /// <summary>Rebuilds an entry from storage, normalizing tab order and invariants.</summary>
    public static JournalEntry Restore(DateOnly date, DateTimeOffset createdUtc, DateTimeOffset modifiedUtc,
        IEnumerable<EntryTab> tabs, IEnumerable<Tag> extraTags, DailyInspiration inspiration, bool inspirationPinned)
    {
        var entry = new JournalEntry(date, createdUtc) { ModifiedUtc = modifiedUtc, Inspiration = inspiration, IsInspirationPinned = inspirationPinned };
        var list = tabs.ToList();
        var defaultTab = list.FirstOrDefault(t => t.Tag is null) ?? new EntryTab(null);
        entry._tabs.Add(defaultTab);
        foreach (var tab in list.Where(t => t.Tag is not null && !ReferenceEquals(t, defaultTab)))
        {
            if (entry.FindTab(tab.Tag!) is null) entry._tabs.Add(tab);
        }
        foreach (var t in extraTags) entry._extraTags.Add(t);
        return entry;
    }

    public EntryTab? FindTab(Tag tag) => _tabs.FirstOrDefault(t => t.Tag == tag);

    public EntryTab? FindTab(Guid id) => _tabs.FirstOrDefault(t => t.Id == id);

    /// <summary>Adds a tab for <paramref name="tag"/>, or returns the existing tab with that tag.</summary>
    public EntryTab AddTab(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        var existing = FindTab(tag);
        if (existing is not null) return existing;
        var tab = new EntryTab(tag);
        _tabs.Add(tab);
        _extraTags.Remove(tag);
        return tab;
    }

    public void RemoveTab(Guid tabId)
    {
        var tab = FindTab(tabId) ?? throw new KeyNotFoundException("No such tab.");
        if (tab.IsDefault) throw new InvalidOperationException("The Journal tab can't be removed.");
        _tabs.Remove(tab);
    }

    public void RetagTab(Guid tabId, Tag newTag)
    {
        var tab = FindTab(tabId) ?? throw new KeyNotFoundException("No such tab.");
        if (tab.IsDefault) throw new InvalidOperationException("The Journal tab can't be renamed.");
        if (tab.Tag == newTag)
        {
            tab.Tag = newTag; // allows changing case
            return;
        }
        if (FindTab(newTag) is not null) throw new DuplicateTagException(newTag);
        tab.Tag = newTag;
        _extraTags.Remove(newTag);
    }

    /// <summary>Moves a tab to a new index; the Journal tab always stays first.</summary>
    public void MoveTab(Guid tabId, int newIndex)
    {
        var tab = FindTab(tabId) ?? throw new KeyNotFoundException("No such tab.");
        if (tab.IsDefault) return;
        _tabs.Remove(tab);
        _tabs.Insert(Math.Clamp(newIndex, 1, _tabs.Count), tab);
    }

    /// <summary>Adds an entry-level tag. Returns false when the tag is already present.</summary>
    public bool AddTag(Tag tag) => FindTab(tag) is null && _extraTags.Add(tag);

    /// <summary>Removes an entry-level tag. Tags that belong to a tab can't be removed here (FR-TAG-04).</summary>
    public bool RemoveTag(Tag tag) => _extraTags.Remove(tag);

    public bool IsTabTag(Tag tag) => FindTab(tag) is not null;

    /// <summary>Tab tags (in tab order) followed by extra tags.</summary>
    public IReadOnlyList<Tag> AllTags() =>
        _tabs.Where(t => t.Tag is not null).Select(t => t.Tag!).Concat(_extraTags.OrderBy(t => t.Normalized)).ToList();

    public bool IsEmpty() => _extraTags.Count == 0 && _tabs.All(t => t.Document.IsEmpty());

    public IEnumerable<BlobRef> ReferencedBlobs() => _tabs.SelectMany(t => t.Document.ReferencedBlobs());

    public void Touch(DateTimeOffset nowUtc) => ModifiedUtc = nowUtc;
}
