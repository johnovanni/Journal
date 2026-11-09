using System.Runtime.InteropServices;
using Journal.Core.Security;
using Windows.Security.Credentials;
using Windows.Security.Credentials.UI;
using Windows.Security.Cryptography;

namespace Journal.Infrastructure.Hello;

/// <summary>Windows Hello key credentials via WinRT <see cref="KeyCredentialManager"/> (TPM-backed RSA-2048).</summary>
public sealed class WinRtHelloCredentialApi : IHelloCredentialApi
{
    public async Task<bool> IsSupportedAsync()
    {
        try { return await KeyCredentialManager.IsSupportedAsync(); }
        catch (Exception) { return false; }
    }

    public async Task CreateAsync(string name, nint windowHandle, CancellationToken cancellation = default)
    {
        Foreground.AllowPrompt();
        var result = await KeyCredentialManager.RequestCreateAsync(name, KeyCredentialCreationOption.ReplaceExisting).AsTask(cancellation);
        Check(result.Status);
    }

    public async Task<byte[]> SignAsync(string name, byte[] data, nint windowHandle, CancellationToken cancellation = default)
    {
        var open = await KeyCredentialManager.OpenAsync(name);
        if (open.Status == KeyCredentialStatus.NotFound)
            throw new UnlockFailedException(UnlockFailure.NotAvailable, "This PC has no Windows Hello key for this journal.");
        Check(open.Status);
        Foreground.AllowPrompt();
        var signed = await open.Credential.RequestSignAsync(CryptographicBuffer.CreateFromByteArray(data)).AsTask(cancellation);
        Check(signed.Status);
        CryptographicBuffer.CopyToByteArray(signed.Result, out var bytes);
        return bytes;
    }

    public async Task DeleteAsync(string name)
    {
        try { await KeyCredentialManager.DeleteAsync(name); }
        catch (Exception) { /* already gone */ }
    }

    private static void Check(KeyCredentialStatus status)
    {
        switch (status)
        {
            case KeyCredentialStatus.Success:
                return;
            case KeyCredentialStatus.UserCanceled:
            case KeyCredentialStatus.UserPrefersPassword:
                throw new UnlockFailedException(UnlockFailure.Cancelled, "Windows Hello verification was cancelled.");
            case KeyCredentialStatus.SecurityDeviceLocked:
                throw new UnlockFailedException(UnlockFailure.NotAvailable, "Windows Hello is locked. Sign in to Windows again, then retry.");
            case KeyCredentialStatus.NotFound:
                throw new UnlockFailedException(UnlockFailure.NotAvailable, "This PC has no Windows Hello key for this journal.");
            default:
                throw new UnlockFailedException(UnlockFailure.NotAvailable, $"Windows Hello isn't available ({status}).");
        }
    }
}

/// <summary>Windows Hello consent check via WinRT <see cref="UserConsentVerifier"/>.</summary>
public sealed class WinRtUserConsentApi : IUserConsentApi
{
    public async Task<bool> IsAvailableAsync()
    {
        try { return await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available; }
        catch (Exception) { return false; }
    }

    public async Task<bool> VerifyAsync(string message, nint windowHandle, CancellationToken cancellation = default)
    {
        Foreground.Bring(windowHandle);
        // With a window, Windows opens the dialog over it on the journal's display (ISS-009, option A).
        var request = windowHandle != 0
            ? UserConsentVerifierInterop.RequestVerificationForWindowAsync(windowHandle, message)
            : UserConsentVerifier.RequestVerificationAsync(message);
        var result = await request.AsTask(cancellation);
        return result == UserConsentVerificationResult.Verified;
    }
}

/// <summary>Keeps the Windows Hello prompt from opening behind the journal window (ISS-023).</summary>
internal static partial class Foreground
{
    private const uint AnyProcess = unchecked((uint)-1);   // ASFW_ANY

    /// <summary>For the window-aware consent check: the dialog opens over this window, so it comes forward first.</summary>
    public static void Bring(nint hwnd)
    {
        if (hwnd != 0) SetForegroundWindow(hwnd);
    }

    /// <summary>
    /// For key signing and creation, whose dialog is a separate Windows process with no link to our window:
    /// Windows stops a background process taking the foreground unless the foreground app allows it. The
    /// permission lasts only until the next input, and bringing our own window forward here would work against it.
    /// </summary>
    public static void AllowPrompt() => AllowSetForegroundWindow(AnyProcess);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);
}
