using System.Security.Cryptography;
using Journal.Core.Services;

namespace Journal.Core.Security;

public enum LockReason { User, Idle, SessionLock, Suspend, Exit }

public enum HelloProtection
{
    /// <summary>Windows Hello key credential protects the data key (D5).</summary>
    Strong,
    /// <summary>Fallback: DPAPI + Hello consent check (FR-SEC-04).</summary>
    Weaker,
    /// <summary>No Hello slot; recovery code only (FR-SEC-05).</summary>
    None,
}

public sealed record SetupResult(RecoveryCode RecoveryCode, HelloProtection Hello, string? HelloMessage);

/// <summary>
/// After 5 wrong recovery codes, each further attempt must wait 30 seconds after the last failure (FR-SEC-11).
/// </summary>
public sealed class UnlockThrottle(IClock clock)
{
    public const int FreeAttempts = 5;
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(30);

    private int _failures;
    private DateTimeOffset _lastFailure;

    public int Failures => _failures;

    public bool CanAttempt(out TimeSpan wait)
    {
        wait = TimeSpan.Zero;
        if (_failures < FreeAttempts) return true;
        var elapsed = clock.UtcNow - _lastFailure;
        if (elapsed >= Delay) return true;
        wait = Delay - elapsed;
        return false;
    }

    public void RecordFailure()
    {
        _failures++;
        _lastFailure = clock.UtcNow;
    }

    public void Reset() => _failures = 0;
}

/// <summary>
/// Holds the unlocked data key and vault header for the lifetime of an unlock, and coordinates locking.
/// </summary>
public sealed class VaultSession(
    IVaultHeaderStore headerStore,
    IEnumerable<IKeyProtector> protectors,
    IMachineIdentity machine,
    IClock clock)
{
    private readonly Dictionary<string, IKeyProtector> _protectors = protectors.ToDictionary(p => p.SlotType);
    private readonly List<Func<LockReason, Task>> _beforeLock = [];
    private SecretKey? _dataKey;

    public VaultHeader? Header { get; private set; }
    public bool IsUnlocked => _dataKey is not null;
    public UnlockThrottle Throttle { get; } = new(clock);
    public string MachineName => machine.MachineName;

    public SecretKey DataKey => _dataKey ?? throw new InvalidOperationException("The journal is locked.");

    public event EventHandler? Unlocked;
    public event EventHandler<LockReason>? Locked;

    public bool VaultExists => headerStore.Exists;

    public async Task<VaultHeader> LoadHeaderAsync()
    {
        Header = await headerStore.LoadAsync();
        return Header;
    }

    public bool HasHelloSlotForThisMachine => Header?.HelloSlotFor(machine.MachineName) is not null;

    /// <summary>Registers work to finish (e.g. saving) before the key is wiped.</summary>
    public void RegisterBeforeLock(Func<LockReason, Task> handler) => _beforeLock.Add(handler);

    public void UnregisterBeforeLock(Func<LockReason, Task> handler) => _beforeLock.Remove(handler);

    public async Task UnlockWithHelloAsync(nint windowHandle = 0)
    {
        var header = Header ?? await LoadHeaderAsync();
        var slot = header.HelloSlotFor(machine.MachineName)
                   ?? throw new UnlockFailedException(UnlockFailure.NotAvailable, "Windows Hello isn't set up for this journal on this PC.");
        var protector = ProtectorFor(slot.Type);
        var key = await protector.UnprotectAsync(slot, header, new UnlockContext(WindowHandle: windowHandle));
        Open(header, key);
    }

    public async Task UnlockWithRecoveryCodeAsync(RecoveryCode code)
    {
        if (!Throttle.CanAttempt(out var wait))
            throw new UnlockFailedException(UnlockFailure.Throttled,
                $"Too many attempts. Try again in {Math.Ceiling(wait.TotalSeconds)} seconds.");
        var header = Header ?? await LoadHeaderAsync();
        var protector = ProtectorFor(KeySlotTypes.RecoveryCode);
        foreach (var slot in header.SlotsOfType(KeySlotTypes.RecoveryCode))
        {
            try
            {
                var key = await protector.UnprotectAsync(slot, header, new UnlockContext(code));
                Throttle.Reset();
                Open(header, key);
                return;
            }
            catch (UnlockFailedException e) when (e.Reason == UnlockFailure.WrongKey) { }
        }
        Throttle.RecordFailure();
        throw new UnlockFailedException(UnlockFailure.WrongKey, "That recovery code didn't work.");
    }

    /// <summary>Opens a vault that was just created (setup wizard).</summary>
    internal void Open(VaultHeader header, SecretKey key)
    {
        Header = header;
        _dataKey?.Dispose();
        _dataKey = key;
        Unlocked?.Invoke(this, EventArgs.Empty);
    }

    public async Task LockAsync(LockReason reason)
    {
        if (!IsUnlocked) return;
        foreach (var handler in _beforeLock.ToList())
        {
            try { await handler(reason); }
            catch { /* locking must never be blocked by a failing save; errors are reported by the handler */ }
        }
        _dataKey?.Dispose();
        _dataKey = null;
        Locked?.Invoke(this, reason);
    }

    // ----- Key slot management (setup and Settings, FR-SEC-03..07, FR-SEC-12, FR-SEC-18) -----

    public async Task<SetupResult> CreateVaultAsync(bool useHello, nint windowHandle = 0)
    {
        var header = new VaultHeader
        {
            CreatedUtc = clock.UtcNow,
            HelloChallenge = RandomNumberGenerator.GetBytes(32),
        };
        var dataKey = SecretKey.Generate();
        var code = RecoveryCode.Generate();
        header.KeySlots.Add(await ProtectorFor(KeySlotTypes.RecoveryCode)
            .ProtectAsync(dataKey, header, new UnlockContext(code)));

        var (hello, message) = useHello
            ? await TryAddHelloSlotAsync(header, dataKey, windowHandle)
            : (HelloProtection.None, "Windows Hello isn't set up on this PC.");

        await headerStore.SaveAsync(header);
        Open(header, dataKey);
        return new SetupResult(code, hello, message);
    }

    public async Task<(HelloProtection Protection, string? Message)> EnrollHelloAsync(nint windowHandle = 0)
    {
        var header = RequireHeader();
        header.KeySlots.RemoveAll(s => s.Type is KeySlotTypes.WindowsHello or KeySlotTypes.HelloDpapi &&
                                       string.Equals(s.Machine, machine.MachineName, StringComparison.OrdinalIgnoreCase));
        var result = await TryAddHelloSlotAsync(header, DataKey, windowHandle);
        await headerStore.SaveAsync(header);
        return result;
    }

    public async Task<RecoveryCode> RegenerateRecoveryCodeAsync()
    {
        var header = RequireHeader();
        var code = RecoveryCode.Generate();
        var slot = await ProtectorFor(KeySlotTypes.RecoveryCode).ProtectAsync(DataKey, header, new UnlockContext(code));
        header.KeySlots.RemoveAll(s => s.Type == KeySlotTypes.RecoveryCode);
        header.KeySlots.Add(slot);
        await headerStore.SaveAsync(header);
        return code;
    }

    /// <summary>Removes the Windows Hello slot of another PC (FR-SEC-18).</summary>
    public async Task RemoveHelloSlotAsync(string machineName)
    {
        var header = RequireHeader();
        header.KeySlots.RemoveAll(s => s.Type is KeySlotTypes.WindowsHello or KeySlotTypes.HelloDpapi &&
                                       string.Equals(s.Machine, machineName, StringComparison.OrdinalIgnoreCase));
        await headerStore.SaveAsync(header);
    }

    public IReadOnlyList<string> HelloMachines() =>
        Header?.KeySlots.Where(s => s.Type is KeySlotTypes.WindowsHello or KeySlotTypes.HelloDpapi)
            .Select(s => s.Machine ?? "?").Distinct().ToList() ?? [];

    private async Task<(HelloProtection, string?)> TryAddHelloSlotAsync(VaultHeader header, SecretKey dataKey, nint hwnd)
    {
        var ctx = new UnlockContext(WindowHandle: hwnd);
        if (_protectors.TryGetValue(KeySlotTypes.WindowsHello, out var hello) && await hello.IsAvailableAsync())
        {
            try
            {
                header.KeySlots.Add(await hello.ProtectAsync(dataKey, header, ctx));
                return (HelloProtection.Strong, null);
            }
            catch (NonDeterministicSignatureException)
            {
                if (_protectors.TryGetValue(KeySlotTypes.HelloDpapi, out var dpapi) && await dpapi.IsAvailableAsync())
                {
                    header.KeySlots.Add(await dpapi.ProtectAsync(dataKey, header, ctx));
                    return (HelloProtection.Weaker,
                        "Windows Hello on this PC can't create a fixed key, so the journal key is protected by your Windows account and released after a Windows Hello check. This is weaker than the usual protection.");
                }
                return (HelloProtection.None, "Windows Hello on this PC can't be used to protect the journal.");
            }
            catch (UnlockFailedException e)
            {
                return (HelloProtection.None, e.Message);
            }
        }
        return (HelloProtection.None, "Windows Hello isn't set up on this PC. You can turn it on in Windows Settings > Accounts > Sign-in options.");
    }

    private VaultHeader RequireHeader() =>
        IsUnlocked && Header is not null ? Header : throw new InvalidOperationException("The journal is locked.");

    private IKeyProtector ProtectorFor(string slotType) =>
        _protectors.TryGetValue(slotType, out var p)
            ? p
            : throw new UnlockFailedException(UnlockFailure.NotAvailable, $"No unlock method for '{slotType}'.");
}

/// <summary>Locks after a period without input (FR-SEC-13). Null timeout means never.</summary>
public sealed class AutoLockPolicy(IClock clock)
{
    private DateTimeOffset _lastActivity = clock.UtcNow;
    private bool _fired;

    public TimeSpan? Timeout { get; set; } = TimeSpan.FromMinutes(10);

    public event EventHandler? LockRequested;

    public void OnActivity()
    {
        _lastActivity = clock.UtcNow;
        _fired = false;
    }

    public TimeSpan? Remaining => Timeout is { } t ? t - (clock.UtcNow - _lastActivity) : null;

    public bool IsExpired => Timeout is { } t && clock.UtcNow - _lastActivity >= t;

    /// <summary>Called periodically; raises <see cref="LockRequested"/> once when the timeout passes.</summary>
    public void Tick()
    {
        if (_fired || !IsExpired) return;
        _fired = true;
        LockRequested?.Invoke(this, EventArgs.Empty);
    }
}
