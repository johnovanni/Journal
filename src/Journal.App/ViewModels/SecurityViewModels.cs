using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Journal.App.Services;
using Journal.Core.Security;

namespace Journal.App.ViewModels;

public sealed record UnlockedEventArgs(bool UsedRecoveryCode, bool OfferHelloEnrollment);

/// <summary>Unlock window: Windows Hello first, recovery code as the alternative (FR-SEC-08..12).</summary>
public sealed partial class UnlockViewModel : ObservableObject
{
    private readonly VaultSession _session;
    private readonly Func<nint> _windowHandle;

    [ObservableProperty] private string _message = "Unlock your journal with Windows Hello.";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _showRecovery;
    [ObservableProperty] private bool _canUseHello;
    [ObservableProperty] private string _recoveryInput = "";

    public event EventHandler<UnlockedEventArgs>? Unlocked;
    public event EventHandler? OpenOtherRequested;

    public UnlockViewModel(VaultSession session, Func<nint> windowHandle)
    {
        _session = session;
        _windowHandle = windowHandle;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _session.LoadHeaderAsync();
        }
        catch (Exception e) when (e is IOException or Journal.Core.Persistence.UnreadableVaultFileException or Journal.Core.Persistence.UnsupportedFormatVersionException)
        {
            Error = e.Message;
            ShowRecovery = false;
            CanUseHello = false;
            return;
        }
        CanUseHello = _session.HasHelloSlotForThisMachine;
        if (!CanUseHello)
        {
            Message = "Windows Hello isn't set up for this journal on this PC. Enter your recovery code.";
            ShowRecovery = true;
        }
    }

    [RelayCommand]
    private async Task TryHello()
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = "";
        try
        {
            await _session.UnlockWithHelloAsync(_windowHandle());
            Unlocked?.Invoke(this, new UnlockedEventArgs(false, false));
        }
        catch (UnlockFailedException e)
        {
            Error = e.Reason == UnlockFailure.Cancelled
                ? "Windows Hello was cancelled. Try again, or use your recovery code."
                : e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void UseRecoveryCode()
    {
        ShowRecovery = true;
        Error = "";
    }

    [RelayCommand]
    private async Task SubmitRecoveryCode()
    {
        if (IsBusy) return;
        Error = "";
        if (!RecoveryCode.TryParse(RecoveryInput, out var code))
        {
            _session.Throttle.RecordFailure();
            Error = "That recovery code didn't work.";
            return;
        }
        IsBusy = true;
        try
        {
            await _session.UnlockWithRecoveryCodeAsync(code);
            RecoveryInput = "";
            Unlocked?.Invoke(this, new UnlockedEventArgs(true, !_session.HasHelloSlotForThisMachine));
        }
        catch (UnlockFailedException e)
        {
            Error = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenOther() => OpenOtherRequested?.Invoke(this, EventArgs.Empty);
}

public enum SetupStep { Location, Creating, ShowCode, Confirm }

/// <summary>First-run wizard: location, Windows Hello, recovery code (FR-SEC-01..07).</summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly Func<string, VaultSession> _sessionFactory;
    private readonly Func<Task<bool>> _helloAvailable;
    private readonly IDialogService _dialogs;
    private readonly Func<nint> _windowHandle;
    private RecoveryCode? _code;

    public string DefaultFolder { get; }
    public string? OneDriveFolder { get; }
    public bool HasOneDrive => OneDriveFolder is not null;

    [ObservableProperty] private SetupStep _step = SetupStep.Location;
    [ObservableProperty] private string _vaultFolder;
    [ObservableProperty] private bool _helloIsAvailable;
    [ObservableProperty] private string _helloMessage = "";
    [ObservableProperty] private string _recoveryCodeText = "";
    [ObservableProperty] private string _confirmInput = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _weakerProtection;

    public VaultSession? Session { get; private set; }

    public event EventHandler? Completed;
    /// <summary>Raised when the chosen folder already contains a journal; the app should unlock it instead.</summary>
    public event EventHandler<string>? ExistingVaultChosen;
    public Action<string>? CopyToClipboard { get; set; }

    public SetupViewModel(Func<string, VaultSession> sessionFactory, Func<Task<bool>> helloAvailable, IDialogService dialogs,
        Func<nint> windowHandle, string defaultFolder, string? oneDriveFolder)
    {
        _sessionFactory = sessionFactory;
        _helloAvailable = helloAvailable;
        _dialogs = dialogs;
        _windowHandle = windowHandle;
        DefaultFolder = defaultFolder;
        OneDriveFolder = oneDriveFolder;
        _vaultFolder = defaultFolder;
    }

    public async Task InitializeAsync()
    {
        HelloIsAvailable = await _helloAvailable();
        HelloMessage = HelloIsAvailable
            ? "You'll unlock the journal with Windows Hello (fingerprint, face or PIN). There's no password to remember."
            : "Windows Hello isn't set up on this PC. To unlock with your fingerprint, face or PIN, turn it on in Windows Settings > Accounts > Sign-in options. You can continue now and use your recovery code to unlock until then.";
    }

    public bool CanFinish => _code is not null && RecoveryCode.NormalizeGroup(ConfirmInput) == _code.LastGroup;

    partial void OnConfirmInputChanged(string value)
    {
        OnPropertyChanged(nameof(CanFinish));
        FinishCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void UseDefault() => VaultFolder = DefaultFolder;

    [RelayCommand]
    private void UseOneDrive()
    {
        if (OneDriveFolder is not null) VaultFolder = OneDriveFolder;
    }

    [RelayCommand]
    private void Browse()
    {
        var folder = _dialogs.PickFolder("Choose where to keep your journal", VaultFolder);
        if (folder is not null) VaultFolder = Path.Combine(folder, "Journal");
    }

    [RelayCommand]
    private async Task Create()
    {
        Error = "";
        if (File.Exists(Path.Combine(VaultFolder, "vault.json")))
        {
            ExistingVaultChosen?.Invoke(this, VaultFolder);
            return;
        }
        Step = SetupStep.Creating;
        try
        {
            Directory.CreateDirectory(VaultFolder);
            Session = _sessionFactory(VaultFolder);
            var result = await Session.CreateVaultAsync(HelloIsAvailable, _windowHandle());
            _code = result.RecoveryCode;
            RecoveryCodeText = result.RecoveryCode.Formatted;
            WeakerProtection = result.Hello == HelloProtection.Weaker;
            if (result.Hello != HelloProtection.Strong && result.HelloMessage is not null) HelloMessage = result.HelloMessage;
            HelloIsAvailable = result.Hello != HelloProtection.None;
            Step = SetupStep.ShowCode;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = $"The journal couldn't be created there: {e.Message}";
            Step = SetupStep.Location;
        }
    }

    [RelayCommand]
    private void CopyCode() => CopyToClipboard?.Invoke(RecoveryCodeText);

    [RelayCommand]
    private void SaveCode()
    {
        var path = _dialogs.SaveFile("Save recovery code", "Journal recovery code.txt", "Text file|*.txt");
        if (path is null) return;
        File.WriteAllText(path, RecoveryCodeFile(RecoveryCodeText));
    }

    public static string RecoveryCodeFile(string code) =>
        $"Journal recovery code{Environment.NewLine}{Environment.NewLine}{code}{Environment.NewLine}{Environment.NewLine}" +
        "Use this code to open your journal if Windows Hello on this PC is ever lost (new PC, Windows reinstall)." + Environment.NewLine +
        "Keep it somewhere safe, away from this PC. Anyone with this code and a copy of your journal folder can read it.";

    [RelayCommand]
    private void ContinueToConfirm() => Step = SetupStep.Confirm;

    [RelayCommand]
    private void BackToCode() => Step = SetupStep.ShowCode;

    [RelayCommand(CanExecute = nameof(CanFinish))]
    private void Finish() => Completed?.Invoke(this, EventArgs.Empty);
}
