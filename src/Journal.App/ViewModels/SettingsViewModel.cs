using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Journal.App.Services;
using Journal.Core.Inspiration;
using Journal.Core.Security;
using Journal.Core.Settings;

namespace Journal.App.ViewModels;

public sealed partial class CheckItem(string name, bool isChecked) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private bool _isChecked = isChecked;
}

public sealed record AutoLockOption(int Minutes, string Label);

/// <summary>Actions the settings window needs from the running app.</summary>
public interface ISettingsHost
{
    VaultSession Session { get; }
    string VaultFolder { get; }
    IReadOnlyList<string> AvailableBooks { get; }
    IReadOnlyList<string> AvailableTraditions { get; }
    int ImportedVerseCount { get; }
    int ImportedQuoteCount { get; }
    Task<string> ImportVersesAsync(string path);
    Task<string> ImportQuotesAsync(string path);
    Task MoveVaultAsync(string newFolder);
    void ExportBackup(string zipPath);
    Task ApplySettingsAsync(AppSettings settings);
}

/// <summary>Settings window (FR-SET-01, FR-SEC-13, FR-SEC-18, FR-CAL-02, FR-INSP-05..11).</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _host;
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;

    public IReadOnlyList<AutoLockOption> AutoLockOptions { get; } =
    [
        new(1, "1 minute"), new(5, "5 minutes"), new(10, "10 minutes"), new(15, "15 minutes"),
        new(30, "30 minutes"), new(60, "1 hour"), new(120, "2 hours"), new(0, "Never"),
    ];

    public IReadOnlyList<DayOfWeek> WeekStarts { get; } = [DayOfWeek.Sunday, DayOfWeek.Monday];

    [ObservableProperty] private AutoLockOption _autoLock;
    [ObservableProperty] private DayOfWeek _weekStart;
    [ObservableProperty] private string _vaultFolder;
    [ObservableProperty] private bool _showVerse;
    [ObservableProperty] private bool _showQuote;
    [ObservableProperty] private bool _useImportedVerses;
    [ObservableProperty] private bool _useImportedQuotes;
    [ObservableProperty] private bool _randomPerDay;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _importedVersesText = "";
    [ObservableProperty] private string _importedQuotesText = "";

    public ObservableCollection<CheckItem> Books { get; } = [];
    public ObservableCollection<CheckItem> Traditions { get; } = [];
    public ObservableCollection<string> HelloMachines { get; } = [];
    public string ThisMachine => _host.Session.MachineName;

    public SettingsViewModel(ISettingsHost host, IDialogService dialogs, AppSettings current)
    {
        _host = host;
        _dialogs = dialogs;
        _settings = current;
        _autoLock = AutoLockOptions.FirstOrDefault(o => o.Minutes == current.AutoLockMinutes) ?? AutoLockOptions[2];
        _weekStart = current.WeekStart;
        _vaultFolder = host.VaultFolder;
        var insp = current.Inspiration;
        _showVerse = insp.ShowVerse;
        _showQuote = insp.ShowQuote;
        _useImportedVerses = insp.VerseSource == SourceNames.Imported;
        _useImportedQuotes = insp.QuoteSource == SourceNames.Imported;
        _randomPerDay = insp.Mode == SelectionMode.RandomPerDay;
        foreach (var b in host.AvailableBooks) Books.Add(new CheckItem(b, insp.VerseBooks.Contains(b, StringComparer.OrdinalIgnoreCase)));
        foreach (var t in host.AvailableTraditions) Traditions.Add(new CheckItem(t, insp.Traditions.Contains(t, StringComparer.OrdinalIgnoreCase)));
        RefreshMachines();
        RefreshImportCounts();
    }

    private void RefreshMachines()
    {
        HelloMachines.Clear();
        foreach (var m in _host.Session.HelloMachines()) HelloMachines.Add(m);
    }

    private void RefreshImportCounts()
    {
        ImportedVersesText = _host.ImportedVerseCount == 0 ? "No verses imported" : $"{_host.ImportedVerseCount} imported verses";
        ImportedQuotesText = _host.ImportedQuoteCount == 0 ? "No quotes imported" : $"{_host.ImportedQuoteCount} imported quotes";
    }

    /// <summary>Builds the settings to save from the current form state.</summary>
    public AppSettings BuildSettings()
    {
        _settings.AutoLockMinutes = AutoLock.Minutes;
        _settings.WeekStart = WeekStart;
        _settings.Inspiration = new InspirationOptions
        {
            ShowVerse = ShowVerse,
            ShowQuote = ShowQuote,
            VerseSource = UseImportedVerses ? SourceNames.Imported : SourceNames.BuiltIn,
            QuoteSource = UseImportedQuotes ? SourceNames.Imported : SourceNames.BuiltIn,
            VerseBooks = Books.Where(b => b.IsChecked).Select(b => b.Name).ToList(),
            Traditions = Traditions.Where(t => t.IsChecked).Select(t => t.Name).ToList(),
            Mode = RandomPerDay ? SelectionMode.RandomPerDay : SelectionMode.DayOfYear,
        };
        return _settings;
    }

    [RelayCommand]
    private async Task Save()
    {
        await _host.ApplySettingsAsync(BuildSettings());
        Status = "Settings saved.";
    }

    [RelayCommand]
    private async Task ImportVerses()
    {
        var path = _dialogs.OpenFile("Import verses", "Verse lists|*.csv;*.json|All files|*.*");
        if (path is null) return;
        try
        {
            Status = await _host.ImportVersesAsync(path);
            UseImportedVerses = true;
        }
        catch (Exception e) when (e is ImportException or IOException)
        {
            _dialogs.Error("Import verses", e.Message);
        }
        RefreshImportCounts();
    }

    [RelayCommand]
    private async Task ImportQuotes()
    {
        var path = _dialogs.OpenFile("Import quotes", "Quote lists|*.csv;*.json|All files|*.*");
        if (path is null) return;
        try
        {
            Status = await _host.ImportQuotesAsync(path);
            UseImportedQuotes = true;
        }
        catch (Exception e) when (e is ImportException or IOException)
        {
            _dialogs.Error("Import quotes", e.Message);
        }
        RefreshImportCounts();
    }

    [RelayCommand]
    private async Task RegenerateRecoveryCode()
    {
        if (!_dialogs.Confirm("New recovery code",
                "Create a new recovery code? Your old code will stop working.", "Create new code"))
            return;
        var code = await _host.Session.RegenerateRecoveryCodeAsync();
        _dialogs.ShowRecoveryCode(code.Formatted);
        Status = "New recovery code created. The old code no longer works.";
    }

    [RelayCommand]
    private async Task EnrollHello()
    {
        var (protection, message) = await _host.Session.EnrollHelloAsync();
        Status = protection switch
        {
            HelloProtection.Strong => "Windows Hello is set up for this PC.",
            HelloProtection.Weaker => message ?? "Windows Hello is set up with weaker protection.",
            _ => message ?? "Windows Hello couldn't be set up.",
        };
        RefreshMachines();
    }

    [RelayCommand]
    private async Task RemoveMachine(string? machine)
    {
        if (machine is null) return;
        if (!_dialogs.Confirm("Remove PC", $"Stop {machine} from unlocking this journal with Windows Hello?", "Remove")) return;
        await _host.Session.RemoveHelloSlotAsync(machine);
        RefreshMachines();
    }

    [RelayCommand]
    private async Task MoveVault()
    {
        var folder = _dialogs.PickFolder("Choose a new location for your journal");
        if (folder is null) return;
        var target = Path.Combine(folder, "Journal");
        try
        {
            await _host.MoveVaultAsync(target);
            VaultFolder = target;
            Status = "Journal moved.";
        }
        catch (IOException e)
        {
            _dialogs.Error("Move journal", $"The journal wasn't moved and is still in its old location. {e.Message}");
        }
    }

    [RelayCommand]
    private void ExportBackup()
    {
        var path = _dialogs.SaveFile("Export backup", $"Journal backup {DateTime.Now:yyyy-MM-dd}.zip", "Zip file|*.zip");
        if (path is null) return;
        _host.ExportBackup(path);
        Status = "Backup exported. It stays encrypted; you'll need your recovery code to open it on another PC.";
    }
}
