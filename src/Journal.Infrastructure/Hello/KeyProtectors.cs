using System.Security.Cryptography;
using Journal.Core.Security;
using Journal.Infrastructure.Crypto;

namespace Journal.Infrastructure.Hello;

/// <summary>Testable wrapper around the Windows Hello key-credential API.</summary>
public interface IHelloCredentialApi
{
    Task<bool> IsSupportedAsync();
    /// <summary>Creates (or replaces) a TPM-backed key; prompts the user. Throws <see cref="UnlockFailedException"/>.</summary>
    Task CreateAsync(string name, nint windowHandle);
    /// <summary>Signs <paramref name="data"/> after a Windows Hello check. Throws <see cref="UnlockFailedException"/>.</summary>
    Task<byte[]> SignAsync(string name, byte[] data, nint windowHandle);
    Task DeleteAsync(string name);
}

/// <summary>Testable wrapper around Windows Hello user-consent verification.</summary>
public interface IUserConsentApi
{
    Task<bool> IsAvailableAsync();
    /// <summary>Returns true when the user passed the check.</summary>
    Task<bool> VerifyAsync(string message, nint windowHandle);
}

/// <summary>
/// Primary unlock (D5): a Windows Hello key signs the vault challenge; HKDF over the signature gives the
/// key that wraps the data key.
/// </summary>
public sealed class WindowsHelloKeyProtector(IHelloCredentialApi hello, IMachineIdentity machine) : IKeyProtector
{
    public string SlotType => KeySlotTypes.WindowsHello;

    public Task<bool> IsAvailableAsync() => hello.IsSupportedAsync();

    public static string CredentialName(Guid vaultId) => $"Journal.Vault.{vaultId:N}";

    public async Task<KeySlot> ProtectAsync(SecretKey dataKey, VaultHeader header, UnlockContext context)
    {
        var name = CredentialName(header.VaultId);
        await hello.CreateAsync(name, context.WindowHandle);
        var first = await hello.SignAsync(name, header.HelloChallenge, context.WindowHandle);
        var second = await hello.SignAsync(name, header.HelloChallenge, context.WindowHandle);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(first, second))
            {
                await hello.DeleteAsync(name);
                throw new NonDeterministicSignatureException();
            }
            using var kek = KeyDerivation.HelloKek(first, header.HelloChallenge);
            return new KeySlot
            {
                Type = SlotType,
                CredentialName = name,
                Machine = machine.MachineName,
                WrappedKey = KeyWrapper.Wrap(kek, dataKey, header.SlotAad(SlotType)),
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(first);
            CryptographicOperations.ZeroMemory(second);
        }
    }

    public async Task<SecretKey> UnprotectAsync(KeySlot slot, VaultHeader header, UnlockContext context)
    {
        var signature = await hello.SignAsync(slot.CredentialName ?? CredentialName(header.VaultId), header.HelloChallenge, context.WindowHandle);
        try
        {
            using var kek = KeyDerivation.HelloKek(signature, header.HelloChallenge);
            return KeyWrapper.Unwrap(kek, slot.WrappedKey, header.SlotAad(SlotType));
        }
        catch (CryptographicException e)
        {
            throw new UnlockFailedException(UnlockFailure.WrongKey,
                "This PC's Windows Hello key doesn't open this journal. Use your recovery code, then set up Windows Hello again.", e);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }
}

/// <summary>
/// Fallback (FR-SEC-04): the data key is protected with DPAPI for the current Windows user and released only
/// after a Windows Hello consent check. Weaker, because the check is enforced by the app, not by cryptography.
/// </summary>
public sealed class HelloGatedDpapiProtector(IUserConsentApi consent, IMachineIdentity machine) : IKeyProtector
{
    public string SlotType => KeySlotTypes.HelloDpapi;

    public Task<bool> IsAvailableAsync() => consent.IsAvailableAsync();

    public Task<KeySlot> ProtectAsync(SecretKey dataKey, VaultHeader header, UnlockContext context)
    {
        var protectedKey = ProtectedData.Protect(dataKey.Span.ToArray(), Entropy(header), DataProtectionScope.CurrentUser);
        return Task.FromResult(new KeySlot { Type = SlotType, Machine = machine.MachineName, WrappedKey = protectedKey });
    }

    public async Task<SecretKey> UnprotectAsync(KeySlot slot, VaultHeader header, UnlockContext context)
    {
        if (!await consent.VerifyAsync("Unlock your journal", context.WindowHandle))
            throw new UnlockFailedException(UnlockFailure.Cancelled, "Windows Hello verification was cancelled.");
        byte[] plain;
        try
        {
            plain = ProtectedData.Unprotect(slot.WrappedKey, Entropy(header), DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException e)
        {
            throw new UnlockFailedException(UnlockFailure.WrongKey, "This journal's key can't be opened by this Windows account.", e);
        }
        try { return SecretKey.FromBytes(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static byte[] Entropy(VaultHeader header) => [.. header.HelloChallenge, .. header.VaultId.ToByteArray()];
}

/// <summary>Backup unlock (D6): PBKDF2 over the recovery code derives the key that wraps the data key.</summary>
public sealed class RecoveryCodeKeyProtector(int iterations = KeyDerivation.RecoveryIterations) : IKeyProtector
{
    public string SlotType => KeySlotTypes.RecoveryCode;

    public Task<bool> IsAvailableAsync() => Task.FromResult(true);

    public Task<KeySlot> ProtectAsync(SecretKey dataKey, VaultHeader header, UnlockContext context)
    {
        var code = context.RecoveryCode ?? throw new ArgumentException("A recovery code is required.", nameof(context));
        return Task.Run(() =>
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            using var kek = KeyDerivation.RecoveryKek(code, salt, iterations);
            return new KeySlot
            {
                Type = SlotType,
                Kdf = "PBKDF2-SHA256",
                Iterations = iterations,
                Salt = salt,
                WrappedKey = KeyWrapper.Wrap(kek, dataKey, header.SlotAad(SlotType)),
            };
        });
    }

    public Task<SecretKey> UnprotectAsync(KeySlot slot, VaultHeader header, UnlockContext context)
    {
        var code = context.RecoveryCode
                   ?? throw new UnlockFailedException(UnlockFailure.WrongKey, "Enter your recovery code.");
        if (slot.Salt is null || slot.Iterations is null)
            throw new UnlockFailedException(UnlockFailure.WrongKey, "The recovery slot is damaged.");
        return Task.Run(() =>
        {
            using var kek = KeyDerivation.RecoveryKek(code, slot.Salt, slot.Iterations.Value);
            try
            {
                return KeyWrapper.Unwrap(kek, slot.WrappedKey, header.SlotAad(SlotType));
            }
            catch (CryptographicException e)
            {
                throw new UnlockFailedException(UnlockFailure.WrongKey, "That recovery code didn't work.", e);
            }
        });
    }
}
