namespace Journal.Core.Security;

public static class KeySlotTypes
{
    public const string WindowsHello = "windows-hello";
    public const string HelloDpapi = "hello-dpapi";
    public const string RecoveryCode = "recovery-code";
}

/// <summary>One wrapped copy of the data key, for one unlock method (Design §6.2).</summary>
public sealed record KeySlot
{
    public required string Type { get; init; }
    public string? CredentialName { get; init; }
    public string? Machine { get; init; }
    public string? Kdf { get; init; }
    public int? Iterations { get; init; }
    public byte[]? Salt { get; init; }
    public required byte[] WrappedKey { get; init; }
}

/// <summary>Plaintext vault header (vault.json). Contains no journal content.</summary>
public sealed class VaultHeader
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public Guid VaultId { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedUtc { get; set; }
    /// <summary>Random 32-byte challenge signed by the Windows Hello key.</summary>
    public byte[] HelloChallenge { get; set; } = [];
    public List<KeySlot> KeySlots { get; set; } = [];

    public IEnumerable<KeySlot> SlotsOfType(string type) => KeySlots.Where(s => s.Type == type);

    public KeySlot? HelloSlotFor(string machine) =>
        KeySlots.FirstOrDefault(s => s.Type is KeySlotTypes.WindowsHello or KeySlotTypes.HelloDpapi &&
                                     string.Equals(s.Machine, machine, StringComparison.OrdinalIgnoreCase));

    /// <summary>Associated data that binds a wrapped key to this vault and slot type.</summary>
    public byte[] SlotAad(string slotType) => System.Text.Encoding.UTF8.GetBytes($"journal/slot/{VaultId:N}/{slotType}");
}

public interface IVaultHeaderStore
{
    bool Exists { get; }
    Task<VaultHeader> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(VaultHeader header, CancellationToken ct = default);
}

public enum UnlockFailure
{
    /// <summary>The user cancelled the Windows Hello prompt.</summary>
    Cancelled,
    /// <summary>Windows Hello isn't set up or this vault has no Hello key for this PC.</summary>
    NotAvailable,
    /// <summary>The key didn't open the vault (wrong recovery code or damaged slot).</summary>
    WrongKey,
    /// <summary>Too many wrong attempts; wait and try again.</summary>
    Throttled,
}

public sealed class UnlockFailedException(UnlockFailure reason, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public UnlockFailure Reason { get; } = reason;
}

/// <summary>Thrown by the Hello protector during setup when signatures are not repeatable (FR-SEC-04).</summary>
public sealed class NonDeterministicSignatureException()
    : Exception("Windows Hello produced different signatures for the same data, so it can't be used to derive a key.");

/// <summary>Extra input an unlock method may need.</summary>
public sealed record UnlockContext(RecoveryCode? RecoveryCode = null, nint WindowHandle = 0);

/// <summary>
/// Strategy for protecting the data key with one unlock method (Windows Hello, recovery code, ...).
/// </summary>
public interface IKeyProtector
{
    string SlotType { get; }
    Task<bool> IsAvailableAsync();
    /// <summary>Wraps the data key, creating any credential needed. Returns the new slot.</summary>
    Task<KeySlot> ProtectAsync(SecretKey dataKey, VaultHeader header, UnlockContext context);
    /// <summary>Unwraps the data key from <paramref name="slot"/>. Throws <see cref="UnlockFailedException"/>.</summary>
    Task<SecretKey> UnprotectAsync(KeySlot slot, VaultHeader header, UnlockContext context);
}

/// <summary>Abstraction of the PC identity so tests can simulate a second machine.</summary>
public interface IMachineIdentity
{
    string MachineName { get; }
}

public sealed class EnvironmentMachineIdentity : IMachineIdentity
{
    public string MachineName => Environment.MachineName;
}
