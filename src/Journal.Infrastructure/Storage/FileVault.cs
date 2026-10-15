using System.Security.Cryptography;
using System.Text.Json;
using Journal.Core.Documents;
using Journal.Core.Domain;
using Journal.Core.Persistence;
using Journal.Core.Security;
using Journal.Infrastructure.Crypto;

namespace Journal.Infrastructure.Storage;

/// <summary>Locations inside a vault folder (Design §6.2).</summary>
public sealed class VaultPaths(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string Header => Path.Combine(Root, "vault.json");
    public string Catalog => Path.Combine(Root, "catalog.jcat");
    public string Entries => Path.Combine(Root, "entries");
    public string Blobs => Path.Combine(Root, "blobs");
    public string Lists => Path.Combine(Root, "lists");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Entries);
        Directory.CreateDirectory(Blobs);
        Directory.CreateDirectory(Lists);
    }
}

/// <summary>
/// Writes a temp file, flushes it to disk and renames it over the target, so a crash never leaves a
/// half-written file (NFR-REL-01).
/// </summary>
public class AtomicFileWriter
{
    public const string TempSuffix = ".tmp";

    /// <summary>Test hook: runs after the temp file is written and before the rename.</summary>
    public Func<string, Task>? BeforeRename { get; set; }

    public Task WriteAsync(string path, byte[] data, CancellationToken ct = default) =>
        WriteAsync(path, async s => await s.WriteAsync(data, ct), ct);

    public async Task WriteAsync(string path, Func<Stream, Task> write, CancellationToken ct = default)
    {
        var temp = path + TempSuffix;
        try
        {
            await using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await write(fs);
                await fs.FlushAsync(ct);
                fs.Flush(flushToDisk: true);
            }
            if (BeforeRename is not null) await BeforeRename(temp);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Removes temp files left behind by an interrupted write.</summary>
    public static int CleanupTempFiles(string root)
    {
        if (!Directory.Exists(root)) return 0;
        int n = 0;
        foreach (var f in Directory.EnumerateFiles(root, "*" + TempSuffix, SearchOption.AllDirectories))
        {
            if (TryDelete(f)) n++;
        }
        return n;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}

public sealed class JsonVaultHeaderStore(VaultPaths paths, AtomicFileWriter writer) : IVaultHeaderStore
{
    private static readonly JsonSerializerOptions Options = new(JournalJson.Options) { WriteIndented = true };

    public bool Exists => File.Exists(paths.Header);

    public async Task<VaultHeader> LoadAsync(CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(paths.Header, ct);
        VaultHeader header;
        try
        {
            header = JsonSerializer.Deserialize<VaultHeader>(bytes, Options) ?? throw new JsonException("Empty header.");
        }
        catch (JsonException e)
        {
            throw new UnreadableVaultFileException("vault header", e);
        }
        if (header.FormatVersion > VaultHeader.CurrentFormatVersion)
            throw new UnsupportedFormatVersionException("journal", header.FormatVersion);
        return header;
    }

    public async Task SaveAsync(VaultHeader header, CancellationToken ct = default)
    {
        Directory.CreateDirectory(paths.Root);
        paths.EnsureCreated();
        await writer.WriteAsync(paths.Header, JsonSerializer.SerializeToUtf8Bytes(header, Options), ct);
    }
}

/// <summary>One encrypted file per entry, named by HMAC of the date (D7, NFR-SEC-04).</summary>
public sealed class FileEntryRepository(VaultPaths paths, VaultCrypto crypto, AtomicFileWriter writer) : IEntryRepository
{
    public const string Extension = ".jent";

    public string PathFor(DateOnly date) => Path.Combine(paths.Entries, crypto.EntryFileName(date) + Extension);

    public async Task<JournalEntry?> GetAsync(DateOnly date, CancellationToken ct = default)
    {
        var path = PathFor(date);
        if (!File.Exists(path)) return null;
        var entry = await ReadAsync(path, ct);
        if (entry.Date != date) throw new UnreadableVaultFileException("entry");
        return entry;
    }

    public async Task SaveAsync(JournalEntry entry, CancellationToken ct = default)
    {
        paths.EnsureCreated();
        var path = PathFor(entry.Date);
        var json = JournalJson.SerializeEntry(entry);
        try
        {
            await writer.WriteAsync(path, crypto.Seal(FileKind.Entry, Path.GetFileNameWithoutExtension(path), json), ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    public Task DeleteAsync(DateOnly date, CancellationToken ct = default)
    {
        var path = PathFor(date);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<(IReadOnlyList<JournalEntry> Entries, int Unreadable)> LoadAllAsync(CancellationToken ct = default)
    {
        var list = new List<JournalEntry>();
        int unreadable = 0;
        if (!Directory.Exists(paths.Entries)) return (list, 0);
        foreach (var file in Directory.EnumerateFiles(paths.Entries, "*" + Extension))
        {
            try
            {
                var entry = await ReadAsync(file, ct);
                // A file copied under another date's name decrypts only if names match (AAD), but check anyway.
                if (Path.GetFileNameWithoutExtension(file) == crypto.EntryFileName(entry.Date)) list.Add(entry);
                else unreadable++;
            }
            catch (UnreadableVaultFileException) { unreadable++; }
            catch (UnsupportedFormatVersionException) { unreadable++; }
        }
        return (list, unreadable);
    }

    private async Task<JournalEntry> ReadAsync(string path, CancellationToken ct)
    {
        var sealedBytes = await File.ReadAllBytesAsync(path, ct);
        byte[] json;
        try
        {
            json = crypto.Open(FileKind.Entry, Path.GetFileNameWithoutExtension(path), sealedBytes);
        }
        catch (CryptographicException e)
        {
            throw new UnreadableVaultFileException("entry", e);
        }
        try
        {
            return JournalJson.DeserializeEntry(json);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or FormatException)
        {
            throw new UnreadableVaultFileException("entry", e);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }
}

/// <summary>Encrypted images and data files, streamed in 1 MB chunks (NFR-PERF-05).</summary>
public sealed class FileBlobStore(VaultPaths paths, VaultCrypto crypto, AtomicFileWriter writer) : IBlobStore
{
    public const string Extension = ".jblob";

    public string PathFor(Guid id) => Path.Combine(paths.Blobs, id.ToString("N") + Extension);

    public async Task<BlobRef> PutAsync(Stream content, CancellationToken ct = default)
    {
        paths.EnsureCreated();
        var id = Guid.NewGuid();
        string sha = "";
        await writer.WriteAsync(PathFor(id), async fs =>
        {
            (sha, _) = await crypto.EncryptStreamAsync(content, fs, id.ToString("N"), ct);
        }, ct);
        return new BlobRef(id, sha);
    }

    public async Task CopyToAsync(BlobRef blob, Stream destination, CancellationToken ct = default)
    {
        var path = PathFor(blob.Id);
        if (!File.Exists(path)) throw new FileNotFoundException("This image or file is missing from the journal.");
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            await crypto.DecryptStreamAsync(fs, destination, blob.Id.ToString("N"), ct);
        }
        catch (CryptographicException e)
        {
            throw new UnreadableVaultFileException("image or file", e);
        }
    }

    public Task DeleteAsync(Guid blobId, CancellationToken ct = default)
    {
        var path = PathFor(blobId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ListAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Guid> ids = !Directory.Exists(paths.Blobs)
            ? []
            : Directory.EnumerateFiles(paths.Blobs, "*" + Extension)
                .Select(f => Guid.TryParseExact(Path.GetFileNameWithoutExtension(f), "N", out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty).ToList();
        return Task.FromResult(ids);
    }

    public bool Exists(Guid blobId) => File.Exists(PathFor(blobId));
}

public sealed class FileCatalogStore(VaultPaths paths, VaultCrypto crypto, AtomicFileWriter writer) : ICatalogStore
{
    private const string LogicalId = "catalog";

    private sealed class CatalogDto
    {
        public int FormatVersion { get; set; } = Catalog.FormatVersion;
        public List<CatalogEntry> Entries { get; set; } = [];
    }

    public async Task<Catalog?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(paths.Catalog)) return null;
        byte[] json;
        try
        {
            json = crypto.Open(FileKind.Catalog, LogicalId, await File.ReadAllBytesAsync(paths.Catalog, ct));
        }
        catch (CryptographicException e)
        {
            throw new UnreadableVaultFileException("catalog", e);
        }
        try
        {
            var dto = JsonSerializer.Deserialize<CatalogDto>(json, JournalJson.Options) ?? throw new JsonException("Empty catalog.");
            if (dto.FormatVersion > Catalog.FormatVersion) throw new UnreadableVaultFileException("catalog");
            return new Catalog(dto.Entries);
        }
        catch (JsonException e)
        {
            throw new UnreadableVaultFileException("catalog", e);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    public async Task SaveAsync(Catalog catalog, CancellationToken ct = default)
    {
        paths.EnsureCreated();
        var json = JsonSerializer.SerializeToUtf8Bytes(new CatalogDto { Entries = [.. catalog.Entries] }, JournalJson.Options);
        try
        {
            await writer.WriteAsync(paths.Catalog, crypto.Seal(FileKind.Catalog, LogicalId, json), ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }
}

/// <summary>Imported verse and quote lists, encrypted inside the vault (FR-INSP-11).</summary>
public sealed class FileUserListStore(VaultPaths paths, VaultCrypto crypto, AtomicFileWriter writer) : IUserListStore
{
    private sealed class ListDto<T>
    {
        public int FormatVersion { get; set; } = 1;
        public List<T> Items { get; set; } = [];
    }

    public Task<IReadOnlyList<Verse>> LoadVersesAsync(CancellationToken ct = default) => LoadAsync<Verse>("verses", ct);
    public Task SaveVersesAsync(IReadOnlyList<Verse> verses, CancellationToken ct = default) => SaveAsync("verses", verses, ct);
    public Task<IReadOnlyList<Quote>> LoadQuotesAsync(CancellationToken ct = default) => LoadAsync<Quote>("quotes", ct);
    public Task SaveQuotesAsync(IReadOnlyList<Quote> quotes, CancellationToken ct = default) => SaveAsync("quotes", quotes, ct);

    private string PathFor(string name) => Path.Combine(paths.Lists, name + ".jlist");

    private async Task<IReadOnlyList<T>> LoadAsync<T>(string name, CancellationToken ct)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return [];
        try
        {
            var json = crypto.Open(FileKind.UserList, name, await File.ReadAllBytesAsync(path, ct));
            return JsonSerializer.Deserialize<ListDto<T>>(json, JournalJson.Options)?.Items ?? [];
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        {
            throw new UnreadableVaultFileException("imported list", e);
        }
    }

    private async Task SaveAsync<T>(string name, IReadOnlyList<T> items, CancellationToken ct)
    {
        paths.EnsureCreated();
        var json = JsonSerializer.SerializeToUtf8Bytes(new ListDto<T> { Items = [.. items] }, JournalJson.Options);
        await writer.WriteAsync(PathFor(name), crypto.Seal(FileKind.UserList, name, json), ct);
    }
}
