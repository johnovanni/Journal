using Journal.Core.Documents;
using Journal.Core.Domain;

namespace Journal.Core.Persistence;

/// <summary>Raised when a vault file exists but cannot be decrypted or parsed (damaged or tampered).</summary>
public sealed class UnreadableVaultFileException(string what, Exception? inner = null)
    : Exception($"This {what} could not be read; it may be damaged.", inner);

public interface IEntryRepository
{
    Task<JournalEntry?> GetAsync(DateOnly date, CancellationToken ct = default);
    Task SaveAsync(JournalEntry entry, CancellationToken ct = default);
    Task DeleteAsync(DateOnly date, CancellationToken ct = default);
    /// <summary>Loads every readable entry (used to rebuild the catalog). Unreadable files are reported, not thrown.</summary>
    Task<(IReadOnlyList<JournalEntry> Entries, int Unreadable)> LoadAllAsync(CancellationToken ct = default);
}

public interface IBlobStore
{
    /// <summary>Encrypts and stores the content, returning a reference with its SHA-256.</summary>
    Task<BlobRef> PutAsync(Stream content, CancellationToken ct = default);
    /// <summary>Decrypts the blob into <paramref name="destination"/> (streaming).</summary>
    Task CopyToAsync(BlobRef blob, Stream destination, CancellationToken ct = default);
    Task DeleteAsync(Guid blobId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ListAsync(CancellationToken ct = default);
    bool Exists(Guid blobId);
}

public interface ICatalogStore
{
    /// <summary>Returns null when the catalog file does not exist; throws <see cref="UnreadableVaultFileException"/> when damaged.</summary>
    Task<Catalog?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(Catalog catalog, CancellationToken ct = default);
}

/// <summary>Imported verse and quote lists, stored encrypted inside the vault.</summary>
public interface IUserListStore
{
    Task<IReadOnlyList<Verse>> LoadVersesAsync(CancellationToken ct = default);
    Task SaveVersesAsync(IReadOnlyList<Verse> verses, CancellationToken ct = default);
    Task<IReadOnlyList<Quote>> LoadQuotesAsync(CancellationToken ct = default);
    Task SaveQuotesAsync(IReadOnlyList<Quote> quotes, CancellationToken ct = default);
}
