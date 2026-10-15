using Journal.Core.Documents;
using Journal.Core.Domain;
using Journal.Core.Inspiration;
using Journal.Core.Persistence;
using Journal.Core.Search;

namespace Journal.Core.Services;

public enum SaveOutcome { Saved, Deleted, Skipped }

public sealed record EntryLoadResult(JournalEntry? Entry, bool IsUnreadable, string? Error)
{
    public static EntryLoadResult Ok(JournalEntry e) => new(e, false, null);
}

public sealed record InitializeResult(bool CatalogRebuilt, int UnreadableEntries, int BlobsCollected);

public sealed class EntryChangedEventArgs(DateOnly date, bool exists) : EventArgs
{
    public DateOnly Date { get; } = date;
    public bool Exists { get; } = exists;
}

/// <summary>
/// Facade over the vault for the view models: opening and saving days, blobs, catalog, search
/// and daily inspiration (Design §8).
/// </summary>
public sealed class JournalService
{
    private readonly IEntryRepository _entries;
    private readonly IBlobStore _blobs;
    private readonly ICatalogStore _catalogStore;
    private readonly DailyInspirationService _inspiration;
    private readonly IClock _clock;
    private readonly Func<InspirationSettings> _inspirationSettings;
    private readonly HashSet<DateOnly> _unreadable = [];

    public Guid VaultId { get; }
    public Catalog Catalog { get; private set; } = new();
    public SearchService Search { get; }
    public DailyInspirationService Inspiration => _inspiration;

    public event EventHandler<EntryChangedEventArgs>? EntryChanged;

    public JournalService(
        IEntryRepository entries,
        IBlobStore blobs,
        ICatalogStore catalogStore,
        ISearchIndex index,
        DailyInspirationService inspiration,
        IClock clock,
        Func<InspirationSettings> inspirationSettings,
        Guid vaultId)
    {
        _entries = entries;
        _blobs = blobs;
        _catalogStore = catalogStore;
        _inspiration = inspiration;
        _clock = clock;
        _inspirationSettings = inspirationSettings;
        VaultId = vaultId;
        Search = new SearchService(new SearchQueryParser(clock), index);
    }

    /// <summary>Loads (or rebuilds) the catalog, builds the search index and removes orphaned blobs.</summary>
    public async Task<InitializeResult> InitializeAsync(CancellationToken ct = default)
    {
        bool rebuilt = false;
        int unreadable = 0;
        Catalog? catalog;
        try
        {
            catalog = await _catalogStore.LoadAsync(ct);
        }
        catch (UnreadableVaultFileException)
        {
            catalog = null;
        }
        if (catalog is null)
        {
            (catalog, unreadable) = await RebuildCatalogAsync(ct);
            rebuilt = true;
        }
        Catalog = catalog;
        Search.Index.Rebuild(catalog);
        int collected = await CollectGarbageAsync([], ct);
        return new InitializeResult(rebuilt, unreadable, collected);
    }

    private async Task<(Catalog, int)> RebuildCatalogAsync(CancellationToken ct)
    {
        var (all, unreadable) = await _entries.LoadAllAsync(ct);
        var catalog = new Catalog(all.Where(e => !e.IsEmpty()).Select(CatalogEntry.From));
        await _catalogStore.SaveAsync(catalog, ct);
        return (catalog, unreadable);
    }

    /// <summary>Opens a day: the stored entry, or a new empty one with today's verse and quote.</summary>
    public async Task<EntryLoadResult> OpenDayAsync(DateOnly date, CancellationToken ct = default)
    {
        JournalEntry? entry;
        try
        {
            entry = await _entries.GetAsync(date, ct);
            _unreadable.Remove(date);
        }
        catch (Exception e) when (e is UnreadableVaultFileException or UnsupportedFormatVersionException)
        {
            _unreadable.Add(date);
            return new EntryLoadResult(null, true, e.Message);
        }

        var settings = _inspirationSettings();
        if (entry is null)
        {
            entry = JournalEntry.Create(date, _clock.UtcNow);
            entry.Inspiration = _inspiration.For(date, settings, VaultId).Inspiration;
        }
        else if (!entry.IsInspirationPinned)
        {
            entry.Inspiration = _inspiration.For(date, settings, VaultId).Inspiration;
        }
        else if (_inspiration.CompletePassage(entry.Inspiration.Verse) is { } full && !ReferenceEquals(full, entry.Inspiration.Verse))
        {
            // A verse saved before the built-in list was corrected may stop mid-sentence; show the full passage.
            entry.Inspiration = entry.Inspiration with { Verse = full };
        }
        return EntryLoadResult.Ok(entry);
    }

    /// <summary>Verse and quote to show for an entry, honoring the show/hide settings.</summary>
    public DailyInspiration DisplayInspiration(JournalEntry entry) =>
        DailyInspirationService.ApplyVisibility(entry.Inspiration, _inspirationSettings());

    public DailyInspiration Shuffle(JournalEntry entry)
    {
        var settings = _inspirationSettings();
        // Shuffle replaces whichever items are visible and keeps the hidden ones as stored.
        var fresh = _inspiration.Shuffle(entry.Inspiration, settings);
        entry.Inspiration = new DailyInspiration(fresh.Verse ?? entry.Inspiration.Verse, fresh.Quote ?? entry.Inspiration.Quote);
        return DisplayInspiration(entry);
    }

    public async Task<SaveOutcome> SaveAsync(JournalEntry entry, CancellationToken ct = default)
    {
        if (_unreadable.Contains(entry.Date))
            throw new InvalidOperationException("This day's entry could not be read, so it is not overwritten.");

        if (entry.IsEmpty())
        {
            if (!Catalog.Contains(entry.Date)) return SaveOutcome.Skipped;
            await _entries.DeleteAsync(entry.Date, ct);
            Catalog.Remove(entry.Date);
            Search.Index.Remove(entry.Date);
            await _catalogStore.SaveAsync(Catalog, ct);
            EntryChanged?.Invoke(this, new EntryChangedEventArgs(entry.Date, false));
            return SaveOutcome.Deleted;
        }

        entry.IsInspirationPinned = true;
        entry.Touch(_clock.UtcNow);
        await _entries.SaveAsync(entry, ct);
        var ce = CatalogEntry.From(entry);
        Catalog.Upsert(ce);
        Search.Index.Upsert(ce);
        await _catalogStore.SaveAsync(Catalog, ct);
        EntryChanged?.Invoke(this, new EntryChangedEventArgs(entry.Date, true));
        return SaveOutcome.Saved;
    }

    /// <summary>Deletes a day's entry and the images and files only it used (FR-ENT-08).</summary>
    public async Task DeleteEntryAsync(DateOnly date, CancellationToken ct = default)
    {
        var blobIds = Catalog.Get(date)?.BlobIds ?? [];
        await _entries.DeleteAsync(date, ct);
        Catalog.Remove(date);
        Search.Index.Remove(date);
        await _catalogStore.SaveAsync(Catalog, ct);
        _unreadable.Remove(date);
        var stillUsed = Catalog.ReferencedBlobIds();
        foreach (var id in blobIds.Where(id => !stillUsed.Contains(id)))
            await _blobs.DeleteAsync(id, ct);
        EntryChanged?.Invoke(this, new EntryChangedEventArgs(date, false));
    }

    public Task<BlobRef> AddBlobAsync(Stream content, CancellationToken ct = default) => _blobs.PutAsync(content, ct);

    public Task CopyBlobToAsync(BlobRef blob, Stream destination, CancellationToken ct = default) =>
        _blobs.CopyToAsync(blob, destination, ct);

    public async Task<byte[]> ReadBlobAsync(BlobRef blob, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await _blobs.CopyToAsync(blob, ms, ct);
        return ms.ToArray();
    }

    /// <summary>
    /// Deletes blobs that no saved entry references (NFR-REL-03). Runs at unlock and at lock, never while an
    /// editor could still undo a deletion; <paramref name="protectedIds"/> keeps blobs used by open documents.
    /// </summary>
    public async Task<int> CollectGarbageAsync(IEnumerable<Guid> protectedIds, CancellationToken ct = default)
    {
        var keep = Catalog.ReferencedBlobIds();
        keep.UnionWith(protectedIds);
        // Entries that could not be read may reference blobs we can't see; skip collection then.
        if (_unreadable.Count > 0) return 0;
        int removed = 0;
        foreach (var id in await _blobs.ListAsync(ct))
        {
            if (keep.Contains(id)) continue;
            await _blobs.DeleteAsync(id, ct);
            removed++;
        }
        return removed;
    }

    public ISet<DateOnly> EntryDates(DateOnly from, DateOnly to) => Catalog.DatesInRange(from, to);

    public ISet<DateOnly> OpenItemDates(DateOnly from, DateOnly to) => Catalog.DatesWithOpenItems(from, to);

    public IReadOnlyList<string> AllTags() => Catalog.AllTags();

    public bool HasEntry(DateOnly date) => Catalog.Contains(date);
}
