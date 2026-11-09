using System.IO;
using Journal.App.ViewModels;
using Journal.Core.Inspiration;
using Journal.Core.Security;
using Journal.Core.Settings;
using Journal.Infrastructure;
using Journal.Infrastructure.Storage;

namespace Journal.App.Services;

/// <summary>What the Settings actions need to know about the journal that is open.</summary>
public interface IOpenJournal
{
    AppSettings Settings { get; set; }
    string VaultFolder { get; }
    VaultSession? Session { get; }
    VaultServices? Services { get; }
    MainViewModel? Main { get; }
}

/// <summary>Carries out what the Settings window asks for (FR-SET-*, FR-INSP-11).</summary>
public sealed class SettingsActions(
    IOpenJournal journal,
    ISettingsStore store,
    LockCoordinator locks,
    IDialogService dialogs,
    Action closeSettingsWindows,
    Action<string, string>? moveVault = null) : ISettingsHost
{
    private readonly Action<string, string> _moveVault = moveVault ?? VaultMover.Move;

    public VaultSession Session => journal.Session ?? throw new InvalidOperationException("The journal is locked.");
    public string VaultFolder => journal.VaultFolder;
    public IReadOnlyList<VerseTradition> VerseTraditions => journal.Services?.Journal.Inspiration.AvailableVerseTraditions() ?? [];
    public IReadOnlyList<QuoteCategory> QuoteCategories => journal.Services?.Journal.Inspiration.AvailableCategories() ?? [];
    public int ImportedVerseCount => journal.Services?.ImportedVerses.Count ?? 0;
    public int ImportedQuoteCount => journal.Services?.ImportedQuotes.Count ?? 0;

    public async Task<string> ImportVersesAsync(string path)
    {
        var result = ListImporter.ImportVerses(await File.ReadAllTextAsync(path), path);
        await journal.Services!.ImportVersesAsync(result.Items);
        return result.Summary;
    }

    public async Task<string> ImportQuotesAsync(string path)
    {
        var result = ListImporter.ImportQuotes(await File.ReadAllTextAsync(path), path);
        await journal.Services!.ImportQuotesAsync(result.Items);
        return result.Summary;
    }

    public async Task MoveVaultAsync(string newFolder)
    {
        if (journal.Main is { } main) await main.FlushAsync();
        var old = journal.VaultFolder;
        await Task.Run(() => _moveVault(old, newFolder));
        journal.Settings.VaultFolder = newFolder;
        store.Save(journal.Settings);
        dialogs.Info("Journal moved", "Your journal was moved. Unlock it again to continue.");
        closeSettingsWindows();
        await locks.LockAsync(LockReason.User);
    }

    public void ExportBackup(string zipPath) => BackupService.Export(journal.VaultFolder, zipPath);

    public async Task ApplySettingsAsync(AppSettings settings)
    {
        journal.Settings = settings;
        store.Save(settings);
        locks.Timeout = settings.AutoLockTimeout;
        locks.NoteActivity();
        locks.Tick();
        if (journal.Main is { } main)
        {
            main.Calendar.WeekStart = settings.WeekStart;
            if (main.CurrentEntry is { } entry) await entry.RefreshInspirationAsync();
        }
    }
}
