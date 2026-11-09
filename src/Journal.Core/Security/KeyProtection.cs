namespace Journal.Core.Security;

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
/// <param name="Cancellation">Cancels a pending Windows Hello request (closes its dialog).</param>
public sealed record UnlockContext(RecoveryCode? RecoveryCode = null, nint WindowHandle = 0, CancellationToken Cancellation = default);

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
