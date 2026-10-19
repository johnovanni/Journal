using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Journal.App.Controls;
using Journal.App.ViewModels;
using Journal.App.Views;
using Journal.Core.Inspiration;
using Journal.Core.Security;
using Journal.Core.Services;
using Journal.Core.Settings;
using Journal.Infrastructure;
using Journal.Infrastructure.Hello;
using Journal.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Journal.App.Services;

/// <summary>
/// Runs the app's lifecycle: setup or unlock, the unlocked journal, auto-lock and locking (Design §10).
/// Vault-dependent services exist only between unlock and lock.
/// </summary>
public sealed class AppController : ISettingsHost
{
    private readonly ISettingsStore _settingsStore;
    private readonly IClock _clock = new SystemClock();
    private readonly IMachineIdentity _machine = new EnvironmentMachineIdentity();
    private readonly TempFileManager _temp = new(AppPaths.TempRoot);
    private readonly DialogService _dialogs = new();
    private readonly ILogger _log;
    private readonly AutoLockPolicy _autoLock;
    private readonly DispatcherTimer _idleTimer;

    private AppSettings _settings;
    private VaultSession? _session;
    private VaultServices? _services;
    private ImageCache? _images;
    private MainWindow? _main;
    private MainWindowCloser? _closer;
    private MainViewModel? _mainVm;
    private bool _locking;
    private Action? _cancelPendingHello;
    private bool _exiting;
    private bool _shutdownRequested;

    public AppController(ISettingsStore settingsStore, ILogger log)
    {
        _settingsStore = settingsStore;
        _log = log;
        _settings = settingsStore.Load();
        _autoLock = new AutoLockPolicy(_clock);
        _autoLock.LockRequested += async (_, _) => await LockAsync(LockReason.Idle);
        _idleTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => OnIdleTick(), Dispatcher.CurrentDispatcher);
    }

    private string VaultFolderSetting => _settings.VaultFolder ?? AppPaths.DefaultVaultFolder;

    public void Start()
    {
        int leftovers = _temp.CleanAll();
        if (leftovers > 0) _log.LogWarning("Some temporary files could not be removed at startup ({Count}).", leftovers);
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        if (File.Exists(Path.Combine(VaultFolderSetting, "vault.json"))) ShowUnlock(VaultFolderSetting);
        else ShowSetup();
    }

    private VaultSession CreateSession(string folder) =>
        new(new JsonVaultHeaderStore(new VaultPaths(folder), new AtomicFileWriter()),
            VaultServices.DefaultProtectors(_machine), _machine, _clock);

    // ------------------------------------------------------------------ setup and unlock

    private void ShowSetup()
    {
        SetupWindow? window = null;
        bool done = false;
        var vm = new SetupViewModel(CreateSession, () => new WinRtHelloCredentialApi().IsSupportedAsync(), _dialogs,
            () => window?.Handle ?? 0, AppPaths.DefaultVaultFolder, AppPaths.OneDriveFolder);
        window = new SetupWindow(vm);
        _cancelPendingHello = vm.CancelPendingHello;
        vm.Completed += async (_, _) =>
        {
            done = true;
            _settings.VaultFolder = vm.VaultFolder;
            _settingsStore.Save(_settings);
            _session = vm.Session;
            window.Close();
            _log.LogInformation("Vault created.");
            await OpenJournalAsync(offerHello: false);
        };
        vm.ExistingVaultChosen += (_, folder) =>
        {
            done = true;
            _settings.VaultFolder = folder;
            _settingsStore.Save(_settings);
            window.Close();
            ShowUnlock(folder);
        };
        window.Closed += (_, _) => { if (!done) Exit(); };
        window.Show();
    }

    private void ShowUnlock(string folder)
    {
        _session = CreateSession(folder);
        UnlockWindow? window = null;
        bool done = false;
        var vm = new UnlockViewModel(_session, () => window?.Handle ?? 0);
        window = new UnlockWindow(vm);
        _cancelPendingHello = vm.CancelPendingHello;
        vm.Unlocked += async (_, args) =>
        {
            done = true;
            window.Close();
            _log.LogInformation("Unlocked ({Method}).", args.UsedRecoveryCode ? "recovery code" : "Windows Hello");
            await OpenJournalAsync(args.OfferHelloEnrollment);
        };
        vm.OpenOtherRequested += (_, _) =>
        {
            var next = ChooseOtherJournal();
            if (next is null) return;
            done = true;
            window.Close();
            ShowUnlock(next);
        };
        window.Closed += (_, _) => { if (!done) Exit(); };
        Application.Current.MainWindow = window;
        window.Show();
        window.Activate();
    }

    /// <summary>Lets the user pick a journal folder or restore a backup zip (FR-SET-05).</summary>
    private string? ChooseOtherJournal()
    {
        var path = _dialogs.OpenFile("Open a journal or backup", "Journal or backup|vault.json;*.zip");
        if (path is null) return null;
        string folder;
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var parent = _dialogs.PickFolder("Choose where to put the restored journal");
            if (parent is null) return null;
            folder = Path.Combine(parent, "Journal");
            try
            {
                BackupService.Restore(path, folder);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _dialogs.Error("Open backup", e.Message);
                return null;
            }
        }
        else folder = Path.GetDirectoryName(path)!;
        _settings.VaultFolder = folder;
        _settingsStore.Save(_settings);
        return folder;
    }

    private async Task OpenJournalAsync(bool offerHello)
    {
        var session = _session!;
        try
        {
            _services = await VaultServices.OpenAsync(session, VaultFolderSetting, _clock, () => _settings.Inspiration.ToSettings());
        }
        catch (Exception e)
        {
            _log.LogError(e, "Opening the journal failed.");
            _dialogs.Error("Journal", "The journal couldn't be opened. " + e.Message);
            await session.LockAsync(LockReason.User);
            ShowUnlock(VaultFolderSetting);
            return;
        }
        var services = _services;
        _images = new ImageCache(b => services.Journal.ReadBlobAsync(b));
        var images = _images;
        ImageBlockView.Loader = (blob, width) => images.LoadAsync(blob, width);

        var ctx = new JournalContext
        {
            Journal = services.Journal,
            Dialogs = _dialogs,
            Scheduler = new DispatcherScheduler(Dispatcher.CurrentDispatcher),
            Clock = _clock,
            Temp = _temp,
            Shell = new ShellLauncher(),
            Clipboard = new ClipboardService(),
            Images = images,
            Settings = () => _settings,
            SaveSettings = s => _settingsStore.Save(s),
        };
        _mainVm = new MainViewModel(ctx);
        _mainVm.LockRequested += async (_, _) => await LockAsync(LockReason.User);
        _mainVm.SettingsRequested += (_, _) => ShowSettings();
        session.RegisterBeforeLock(BeforeLockAsync);
        session.Locked += OnLocked;

        _main = new MainWindow(_mainVm);
        // One click on close saves, locks and exits (FR-SEC-21, ISS-016).
        _closer = new MainWindowCloser(_main, LockForExitAsync, Exit, OnExitError);
        Application.Current.MainWindow = _main;
        _main.Show();
        _main.Activate();
        await _mainVm.InitializeAsync();

        _autoLock.Timeout = _settings.AutoLockTimeout;
        _autoLock.OnActivity();
        InputManager.Current.PreProcessInput += OnInput;
        _idleTimer.Start();
        OnIdleTick();

        if (offerHello && await new WinRtHelloCredentialApi().IsSupportedAsync() &&
            _dialogs.Confirm("Set up Windows Hello",
                "Unlock this journal on this PC with your fingerprint, face or PIN from now on?", "Set up Windows Hello"))
        {
            var (protection, message) = await session.EnrollHelloAsync(new WindowInteropHelper(_main).Handle);
            if (protection == HelloProtection.None && message is not null) _dialogs.Error("Windows Hello", message);
        }
    }

    // ------------------------------------------------------------------ locking

    private async Task BeforeLockAsync(LockReason reason)
    {
        if (_mainVm is not null) await _mainVm.FlushAsync();
        if (_services is not null)
        {
            int removed = await _services.Journal.CollectGarbageAsync([]);
            if (removed > 0) _log.LogInformation("Removed {Count} unused images or files.", removed);
        }
    }

    public async Task LockAsync(LockReason reason)
    {
        if (_locking || _session is not { IsUnlocked: true }) return;
        _locking = true;
        try
        {
            await _session.LockAsync(reason);
        }
        finally
        {
            _locking = false;
        }
    }

    /// <summary>Clears everything that holds journal content (FR-SEC-17).</summary>
    private void OnLocked(object? sender, LockReason reason)
    {
        _log.LogInformation("Locked ({Reason}).", reason);
        if (sender is VaultSession s)
        {
            s.Locked -= OnLocked;
            s.UnregisterBeforeLock(BeforeLockAsync);
        }
        _idleTimer.Stop();
        InputManager.Current.PreProcessInput -= OnInput;
        ImageBlockView.Loader = null;
        _images?.Clear();
        _images = null;
        if (_main is not null)
        {
            _main.DataContext = null;
            // When exiting, the closer closes the window once locking has finished (ISS-016); closing it here
            // could happen inside the window's own Closing event, which WPF refuses.
            if (_closer is not { IsExiting: true })
            {
                _closer?.Detach();
                _main.Close();
            }
            _main = null;
            _closer = null;
        }
        _mainVm = null;
        _services?.Dispose();
        _services = null;
        _temp.CleanAll();
        if (!_exiting) ShowUnlock(VaultFolderSetting);
    }

    private async Task LockForExitAsync()
    {
        _exiting = true;
        await LockAsync(LockReason.Exit);
    }

    private void OnExitError(Exception e)
    {
        _log.LogError(e, "Saving or locking on exit failed.");
        _dialogs.Error("Closing Journal", "Journal couldn't finish saving before closing. " + e.Message);
    }

    /// <summary>Ends the app. Safe to call more than once.</summary>
    private void Exit()
    {
        _exiting = true;
        if (_shutdownRequested) return;
        _shutdownRequested = true;
        _cancelPendingHello?.Invoke();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _temp.CleanAll();
        Application.Current.Shutdown();
    }

    /// <summary>Called from the app's exit path (e.g. Windows shutting down).</summary>
    public void OnApplicationExit()
    {
        _exiting = true;
        _cancelPendingHello?.Invoke();
        if (_session is { IsUnlocked: true })
        {
            try { LockAsync(LockReason.Exit).GetAwaiter().GetResult(); }
            catch (Exception e) { _log.LogError(e, "Saving on exit failed."); }
        }
        _temp.CleanAll();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            Application.Current.Dispatcher.InvokeAsync(() => LockAsync(LockReason.SessionLock));
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
            Application.Current.Dispatcher.Invoke(() => LockAsync(LockReason.Suspend).GetAwaiter().GetResult());
    }

    private void OnInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is KeyboardEventArgs or MouseButtonEventArgs or MouseWheelEventArgs ||
            e.StagingItem.Input is MouseEventArgs && e.StagingItem.Input.RoutedEvent == Mouse.MouseMoveEvent)
            _autoLock.OnActivity();
    }

    private void OnIdleTick()
    {
        _autoLock.Tick();
        if (_mainVm is null) return;
        _mainVm.LockText = _autoLock.Remaining is { } r
            ? $"Locks after {Math.Max(1, (int)Math.Ceiling(r.TotalMinutes))} min without activity"
            : "Auto-lock is off";
    }

    // ------------------------------------------------------------------ settings

    private void ShowSettings()
    {
        if (_main is null || _session is null) return;
        var settingsCopy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(_settings)) ?? new AppSettings();
        var window = new SettingsWindow(new SettingsViewModel(this, _dialogs, settingsCopy)) { Owner = _main };
        window.ShowDialog();
    }

    public VaultSession Session => _session ?? throw new InvalidOperationException("The journal is locked.");
    public string VaultFolder => VaultFolderSetting;
    public IReadOnlyList<string> AvailableBooks => _services?.Journal.Inspiration.AvailableBooks() ?? [];
    public IReadOnlyList<QuoteCategory> QuoteCategories => _services?.Journal.Inspiration.AvailableCategories() ?? [];
    public int ImportedVerseCount => _services?.ImportedVerses.Count ?? 0;
    public int ImportedQuoteCount => _services?.ImportedQuotes.Count ?? 0;

    public async Task<string> ImportVersesAsync(string path)
    {
        var result = ListImporter.ImportVerses(await File.ReadAllTextAsync(path), path);
        await _services!.ImportVersesAsync(result.Items);
        return result.Summary;
    }

    public async Task<string> ImportQuotesAsync(string path)
    {
        var result = ListImporter.ImportQuotes(await File.ReadAllTextAsync(path), path);
        await _services!.ImportQuotesAsync(result.Items);
        return result.Summary;
    }

    public async Task MoveVaultAsync(string newFolder)
    {
        if (_mainVm is not null) await _mainVm.FlushAsync();
        var old = VaultFolderSetting;
        await Task.Run(() => VaultMover.Move(old, newFolder));
        _settings.VaultFolder = newFolder;
        _settingsStore.Save(_settings);
        _dialogs.Info("Journal moved", "Your journal was moved. Unlock it again to continue.");
        foreach (var w in Application.Current.Windows.OfType<SettingsWindow>().ToList()) w.Close();
        await LockAsync(LockReason.User);
    }

    public void ExportBackup(string zipPath) => BackupService.Export(VaultFolderSetting, zipPath);

    public async Task ApplySettingsAsync(AppSettings settings)
    {
        _settings = settings;
        _settingsStore.Save(settings);
        _autoLock.Timeout = settings.AutoLockTimeout;
        _autoLock.OnActivity();
        OnIdleTick();
        if (_mainVm is not null)
        {
            _mainVm.Calendar.WeekStart = settings.WeekStart;
            if (_mainVm.CurrentEntry is { } entry) await entry.RefreshInspirationAsync();
        }
    }
}
