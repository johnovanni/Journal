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
