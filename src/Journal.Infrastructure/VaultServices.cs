using Journal.Core.Domain;
using Journal.Core.Inspiration;
using Journal.Core.Search;
using Journal.Core.Security;
using Journal.Core.Services;
using Journal.Infrastructure.Content;
using Journal.Infrastructure.Crypto;
using Journal.Infrastructure.Hello;
using Journal.Infrastructure.Storage;

namespace Journal.Infrastructure;

/// <summary>
/// Everything that needs the unlocked data key. Created after unlock and disposed on lock, which wipes
/// the derived keys (Design §10, Composition).
/// </summary>
public sealed class VaultServices : IDisposable
{
    private readonly VaultCrypto _crypto;
    private IReadOnlyList<Verse> _importedVerses = [];
    private IReadOnlyList<Quote> _importedQuotes = [];

    public VaultPaths Paths { get; }
    public JournalService Journal { get; }
    public FileUserListStore UserLists { get; }
    public FileEntryRepository Entries { get; }
    public FileBlobStore Blobs { get; }

    public IReadOnlyList<Verse> ImportedVerses => _importedVerses;
    public IReadOnlyList<Quote> ImportedQuotes => _importedQuotes;

    private VaultServices(VaultPaths paths, VaultCrypto crypto, AtomicFileWriter writer, IClock clock, Func<InspirationSettings> settings)
    {
        Paths = paths;
        _crypto = crypto;
        Entries = new FileEntryRepository(paths, crypto, writer);
        Blobs = new FileBlobStore(paths, crypto, writer);
        UserLists = new FileUserListStore(paths, crypto, writer);
        var inspiration = new DailyInspirationService(
            [new EmbeddedVerseSource(), new ListVerseSource(SourceNames.Imported, () => _importedVerses)],
            [new EmbeddedQuoteSource(), new ListQuoteSource(SourceNames.Imported, () => _importedQuotes)]);
        Journal = new JournalService(Entries, Blobs, new FileCatalogStore(paths, crypto, writer), new InMemorySearchIndex(),
            inspiration, clock, settings, crypto.VaultId);
    }

    public static async Task<VaultServices> OpenAsync(VaultSession session, string vaultFolder, IClock clock,
        Func<InspirationSettings> settings, AtomicFileWriter? writer = null)
    {
        var paths = new VaultPaths(vaultFolder);
        AtomicFileWriter.CleanupTempFiles(paths.Root);
        var crypto = new VaultCrypto(session.DataKey, session.Header!.VaultId);
        var services = new VaultServices(paths, crypto, writer ?? new AtomicFileWriter(), clock, settings);
        try
        {
            await services.ReloadImportedListsAsync();
        }
        catch (Journal.Core.Persistence.UnreadableVaultFileException) { /* imported lists are optional */ }
        await services.Journal.InitializeAsync();
        return services;
    }

    public async Task ReloadImportedListsAsync()
    {
        _importedVerses = await UserLists.LoadVersesAsync();
        _importedQuotes = await UserLists.LoadQuotesAsync();
    }

    public async Task ImportVersesAsync(IReadOnlyList<Verse> verses)
    {
        await UserLists.SaveVersesAsync(verses);
        _importedVerses = verses;
    }

    public async Task ImportQuotesAsync(IReadOnlyList<Quote> quotes)
    {
        await UserLists.SaveQuotesAsync(quotes);
        _importedQuotes = quotes;
    }

    public void Dispose() => _crypto.Dispose();

    /// <summary>The standard unlock methods for this PC.</summary>
    public static IReadOnlyList<IKeyProtector> DefaultProtectors(IMachineIdentity machine) =>
    [
        new WindowsHelloKeyProtector(new WinRtHelloCredentialApi(), machine),
        new HelloGatedDpapiProtector(new WinRtUserConsentApi(), machine),
        new RecoveryCodeKeyProtector(),
    ];
}
