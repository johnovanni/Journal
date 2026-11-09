using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Journal.App.Controls;
using Journal.App.ViewModels;
using Journal.App.Views;
using Journal.Core.Security;
using Journal.Core.Services;
using Journal.Core.Settings;
using Journal.Infrastructure;
using Journal.Infrastructure.Hello;
using Journal.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace Journal.App.Services;

/// <summary>
/// Runs the app's window flow: setup or unlock, then the journal, then back to unlock (Design §10).
/// Vault-dependent services exist only between unlock and lock. Deciding when to lock or exit is
/// <see cref="LockCoordinator"/>'s job; the Settings window's actions are <see cref="SettingsActions"/>'.
/// </summary>
public sealed class AppController : IOpenJournal
{
    private readonly ISettingsStore _settingsStore;
    private readonly IClock _clock = new SystemClock();
    private readonly IMachineIdentity _machine = new EnvironmentMachineIdentity();
    private readonly TempFileManager _temp = new(AppPaths.TempRoot);
    private readonly DialogService _dialogs = new();
    private readonly ILogger _log;
    private readonly LockCoordinator _locks;
    private readonly SettingsActions _settingsActions;

    private AppSettings _settings;
    private VaultSession? _session;
    private VaultServices? _services;
    private ImageCache? _images;
    private MainWindow? _main;
    private MainWindowCloser? _closer;
    private MainViewModel? _mainVm;
    private Action? _cancelPendingHello;

    public AppController(ISettingsStore settingsStore, ILogger log)
    {
        _settingsStore = settingsStore;
        _log = log;
        _settings = settingsStore.Load();
        _locks = new LockCoordinator(_clock, () => _session, EndApp, Dispatcher.CurrentDispatcher);
        _locks.StatusChanged += (_, text) => { if (_mainVm is not null) _mainVm.LockText = text; };
        _locks.LockProblem += (_, problem) => _log.LogWarning("{Problem}", problem);
        _settingsActions = new SettingsActions(this, settingsStore, _locks, _dialogs,
            () => { foreach (var w in Application.Current.Windows.OfType<SettingsWindow>().ToList()) w.Close(); });
    }

    AppSettings IOpenJournal.Settings { get => _settings; set => _settings = value; }
    string IOpenJournal.VaultFolder => VaultFolderSetting;
    VaultSession? IOpenJournal.Session => _session;
    VaultServices? IOpenJournal.Services => _services;
    MainViewModel? IOpenJournal.Main => _mainVm;

    private string VaultFolderSetting => _settings.VaultFolder ?? AppPaths.DefaultVaultFolder;

    public void Start()
    {
        int leftovers = _temp.CleanAll();
        if (leftovers > 0) _log.LogWarning("Some temporary files could not be removed at startup ({Count}).", leftovers);
        _locks.HookSystemEvents();
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
        window.Closed += (_, _) => { if (!done) _locks.Shutdown(); };
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
        window.Closed += (_, _) => { if (!done) _locks.Shutdown(); };
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
        _mainVm.LockRequested += async (_, _) => await _locks.LockAsync(LockReason.User);
        _mainVm.SettingsRequested += (_, _) => ShowSettings();
        session.RegisterBeforeLock(BeforeLockAsync);
        session.Locked += OnLocked;

        _main = new MainWindow(_mainVm);
        // One click on close saves, locks and exits (FR-SEC-21, ISS-016).
        _closer = new MainWindowCloser(_main, _locks.LockForExitAsync, _locks.Shutdown, OnExitError);
        Application.Current.MainWindow = _main;
        _main.Show();
        _main.Activate();
        await _mainVm.InitializeAsync();

        _locks.BeginMonitoring(_settings.AutoLockTimeout);

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

    /// <summary>Clears everything that holds journal content (FR-SEC-17).</summary>
    private void OnLocked(object? sender, LockReason reason)
    {
        _log.LogInformation("Locked ({Reason}).", reason);
        if (sender is VaultSession s)
        {
            s.Locked -= OnLocked;
            s.UnregisterBeforeLock(BeforeLockAsync);
        }
        _locks.EndMonitoring();
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
        if (!_locks.IsExiting) ShowUnlock(VaultFolderSetting);
    }

    private void OnExitError(Exception e)
    {
        _log.LogError(e, "Saving or locking on exit failed.");
        _dialogs.Error("Closing Journal", "Journal couldn't finish saving before closing. " + e.Message);
    }

    /// <summary>The last step of exiting, called once by <see cref="LockCoordinator"/>.</summary>
    private void EndApp()
    {
        _cancelPendingHello?.Invoke();
        _temp.CleanAll();
        Application.Current.Shutdown();
    }

    /// <summary>Called from the app's exit path (e.g. Windows shutting down).</summary>
    public void OnApplicationExit()
    {
        _locks.PrepareExit();
        _cancelPendingHello?.Invoke();
        if (_session is { IsUnlocked: true }) _locks.LockAndWait(LockReason.Exit);
        _temp.CleanAll();
    }

    // ------------------------------------------------------------------ settings

    private void ShowSettings()
    {
        if (_main is null || _session is null) return;
        var settingsCopy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(_settings)) ?? new AppSettings();
        var window = new SettingsWindow(new SettingsViewModel(_settingsActions, _dialogs, settingsCopy)) { Owner = _main };
        window.ShowDialog();
    }
}
