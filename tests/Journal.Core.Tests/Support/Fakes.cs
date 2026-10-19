using System.Security.Cryptography;
using Journal.Core.Documents;
using Journal.Core.Domain;
using Journal.Core.Inspiration;
using Journal.Core.Persistence;
using Journal.Core.Search;
using Journal.Core.Security;
using Journal.Core.Services;

namespace Journal.Core.Tests.Support;

/// <summary>Clock fixed at 2026-10-15 10:00 local unless moved.</summary>
public sealed class FakeClock : IClock
{
    public DateTimeOffset LocalNow { get; set; } = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);
    public DateTimeOffset UtcNow => LocalNow.ToUniversalTime();
    public DateOnly Today => DateOnly.FromDateTime(LocalNow.DateTime);
    public void Advance(TimeSpan by) => LocalNow += by;
}

/// <summary>Scheduler whose time only moves when the test calls <see cref="Advance"/>.</summary>
public sealed class ManualScheduler : IScheduler
{
    private readonly List<(TimeSpan Due, Action Action, Handle Handle)> _items = [];
    public TimeSpan Now { get; private set; }

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var h = new Handle();
        _items.Add((Now + delay, action, h));
        return h;
    }

    public void Advance(TimeSpan by)
    {
        var target = Now + by;
        while (true)
        {
            var next = _items.Where(i => !i.Handle.Cancelled && i.Due <= target).OrderBy(i => i.Due).FirstOrDefault();
            if (next.Action is null) break;
            _items.Remove(next);
            Now = next.Due;
            next.Action();
        }
        Now = target;
        _items.RemoveAll(i => i.Handle.Cancelled);
    }

    public int Pending => _items.Count(i => !i.Handle.Cancelled);

    private sealed class Handle : IDisposable
    {
        public bool Cancelled;
        public void Dispose() => Cancelled = true;
    }
}

public sealed class InMemoryEntryRepository : IEntryRepository
{
    public Dictionary<DateOnly, byte[]> Stored { get; } = [];
    public HashSet<DateOnly> Corrupt { get; } = [];
    public int SaveCount { get; private set; }
    public Exception? FailNextSave { get; set; }

    public Task<JournalEntry?> GetAsync(DateOnly date, CancellationToken ct = default)
    {
        if (Corrupt.Contains(date)) throw new UnreadableVaultFileException("entry");
        return Task.FromResult(Stored.TryGetValue(date, out var json) ? JournalJson.DeserializeEntry(json) : null);
    }

    public Task SaveAsync(JournalEntry entry, CancellationToken ct = default)
    {
        if (FailNextSave is { } e)
        {
            FailNextSave = null;
            throw e;
        }
        SaveCount++;
        Stored[entry.Date] = JournalJson.SerializeEntry(entry);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(DateOnly date, CancellationToken ct = default)
    {
        Stored.Remove(date);
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<JournalEntry> Entries, int Unreadable)> LoadAllAsync(CancellationToken ct = default) =>
        Task.FromResult<(IReadOnlyList<JournalEntry>, int)>((
            Stored.Where(kv => !Corrupt.Contains(kv.Key)).Select(kv => JournalJson.DeserializeEntry(kv.Value)).ToList(),
            Corrupt.Count));
}

public sealed class InMemoryBlobStore : IBlobStore
{
    public Dictionary<Guid, byte[]> Blobs { get; } = [];

    public async Task<BlobRef> PutAsync(Stream content, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var id = Guid.NewGuid();
        Blobs[id] = ms.ToArray();
        return new BlobRef(id, Convert.ToHexStringLower(SHA256.HashData(Blobs[id])));
    }

    public async Task CopyToAsync(BlobRef blob, Stream destination, CancellationToken ct = default) =>
        await destination.WriteAsync(Blobs[blob.Id], ct);

    public Task DeleteAsync(Guid blobId, CancellationToken ct = default)
    {
        Blobs.Remove(blobId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Guid>>(Blobs.Keys.ToList());
    public bool Exists(Guid blobId) => Blobs.ContainsKey(blobId);
}

public sealed class InMemoryCatalogStore : ICatalogStore
{
    public Catalog? Saved { get; set; }
    public bool Corrupt { get; set; }
    public int SaveCount { get; private set; }

    public Task<Catalog?> LoadAsync(CancellationToken ct = default)
    {
        if (Corrupt) throw new UnreadableVaultFileException("catalog");
        return Task.FromResult(Saved is null ? null : new Catalog(Saved.Entries));
    }

    public Task SaveAsync(Catalog catalog, CancellationToken ct = default)
    {
        SaveCount++;
        Saved = new Catalog(catalog.Entries);
        Corrupt = false;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryHeaderStore : IVaultHeaderStore
{
    public VaultHeader? Header { get; set; }
    public bool Exists => Header is not null;
    public Task<VaultHeader> LoadAsync(CancellationToken ct = default) => Task.FromResult(Header ?? throw new FileNotFoundException());
    public Task SaveAsync(VaultHeader header, CancellationToken ct = default)
    {
        Header = header;
        return Task.CompletedTask;
    }
}

public sealed class FakeMachine(string name = "TEST-PC") : IMachineIdentity
{
    public string MachineName { get; set; } = name;
}

/// <summary>Key protector that XORs the data key with a secret; fails when the secret is wrong.</summary>
public sealed class FakeKeyProtector(string slotType, string secret = "open sesame") : IKeyProtector
{
    public string SlotType => slotType;
    public string Secret { get; set; } = secret;
    public bool Available { get; set; } = true;
    public UnlockFailure? FailWith { get; set; }
    public IMachineIdentity? Machine { get; set; }
    /// <summary>When set, UnprotectAsync waits until cancelled, like a Windows Hello dialog nobody answers.</summary>
    public bool WaitForUser { get; set; }
    public int UnprotectCalls { get; private set; }
    public bool SawCancellation { get; private set; }

    public Task<bool> IsAvailableAsync() => Task.FromResult(Available);

    public async Task<KeySlot> ProtectAsync(SecretKey dataKey, VaultHeader header, UnlockContext context)
    {
        if (WaitForUser)
        {
            try { await Task.Delay(Timeout.Infinite, context.Cancellation); }
            catch (OperationCanceledException) { SawCancellation = true; throw; }
        }
        var mask = Mask(context);
        var wrapped = dataKey.Span.ToArray().Select((b, i) => (byte)(b ^ mask[i])).ToArray();
        var check = SHA256.HashData(dataKey.Span);
        return new KeySlot { Type = slotType, Machine = Machine?.MachineName, WrappedKey = [.. wrapped, .. check] };
    }

    public async Task<SecretKey> UnprotectAsync(KeySlot slot, VaultHeader header, UnlockContext context)
    {
        UnprotectCalls++;
        if (WaitForUser)
        {
            try { await Task.Delay(Timeout.Infinite, context.Cancellation); }
            catch (OperationCanceledException) { SawCancellation = true; throw; }
        }
        if (FailWith is { } f) throw new UnlockFailedException(f, $"Fake failure: {f}");
        var mask = Mask(context);
        var key = slot.WrappedKey.Take(32).Select((b, i) => (byte)(b ^ mask[i])).ToArray();
        if (!SHA256.HashData(key).SequenceEqual(slot.WrappedKey.Skip(32)))
            throw new UnlockFailedException(UnlockFailure.WrongKey, "That recovery code didn't work.");
        return SecretKey.FromBytes(key);
    }

    private byte[] Mask(UnlockContext ctx) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ctx.RecoveryCode?.Normalized ?? Secret));
}

public static class TestData
{
    public static readonly DateOnly Today = new(2026, 10, 15);

    public static JournalEntry Entry(DateOnly date, string text = "", params string[] tabTags)
    {
        var e = JournalEntry.Create(date, DateTimeOffset.UnixEpoch);
        if (text.Length > 0) e.DefaultTab.Document.Blocks.Add(ParagraphBlock.OfText(text));
        foreach (var t in tabTags) e.AddTab(Tag.Parse(t));
        return e;
    }

    public static IReadOnlyList<Verse> Verses(int n = 400) =>
        Enumerable.Range(1, n).Select(i => new Verse($"{(i % 3 == 0 ? "Psalm" : "Proverbs")} {i}:1", $"Verse text {i}", "WEB")).ToList();

    /// <summary>Every third quote is Literature (tradition "English"); the rest are Philosophy, Stoic or Greek.</summary>
    public static IReadOnlyList<Quote> Quotes(int n = 400) =>
        Enumerable.Range(1, n).Select(i => i % 3 == 0
            ? new Quote($"Quote text {i}", $"Author {i % 25}", "Work", "English") { Category = QuoteCategories.Literature }
            : new Quote($"Quote text {i}", $"Author {i % 25}", "Work", i % 2 == 0 ? "Stoic" : "Greek")).ToList();

    public static DailyInspirationService Inspiration(IReadOnlyList<Verse>? imported = null) => new(
        [new ListVerseSource(SourceNames.BuiltIn, () => Verses()), new ListVerseSource(SourceNames.Imported, () => imported ?? [])],
        [new ListQuoteSource(SourceNames.BuiltIn, () => Quotes())]);

    /// <summary>A JournalService over in-memory stores.</summary>
    public static (JournalService Service, InMemoryEntryRepository Repo, InMemoryBlobStore Blobs, InMemoryCatalogStore Catalog) Service(
        FakeClock? clock = null, Func<InspirationSettings>? settings = null)
    {
        var repo = new InMemoryEntryRepository();
        var blobs = new InMemoryBlobStore();
        var catalog = new InMemoryCatalogStore();
        var svc = new JournalService(repo, blobs, catalog, new InMemorySearchIndex(), Inspiration(), clock ?? new FakeClock(),
            settings ?? (() => new InspirationSettings()), Guid.Parse("11111111-2222-3333-4444-555555555555"));
        return (svc, repo, blobs, catalog);
    }
}
