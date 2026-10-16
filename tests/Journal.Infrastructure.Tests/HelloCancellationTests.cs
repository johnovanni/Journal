using Journal.Core.Security;

namespace Journal.Infrastructure.Tests;

public class HelloCancellationTests
{
    [Fact(DisplayName = "TC-SEC-22 Cancelling passes through to the Windows Hello request")]
    public async Task CancelReachesHelloApi()
    {
        await using var v = await TempVault.CreateAsync();
        v.Services.Dispose();
        await v.Session.LockAsync(LockReason.User);
        v.Hello.WaitForUser = true;
        var session = v.NewSession();
        using var cts = new CancellationTokenSource();
        var unlock = session.UnlockWithHelloAsync(0, cts.Token);
        cts.Cancel();
        var finished = await Task.WhenAny(unlock, Task.Delay(TimeSpan.FromSeconds(3))) == unlock;
        Assert.True(finished, "the Windows Hello request ignored cancellation");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unlock);
        Assert.True(v.Hello.SawCancellation);
        Assert.False(session.IsUnlocked);
    }

    [Fact(DisplayName = "TC-SEC-25 The fallback unlock passes the journal's window to Windows Hello")]
    public async Task FallbackPassesWindow()
    {
        var v = new TempVault();
        v.Hello.Deterministic = false; // forces the consent-check fallback
        var session = v.NewSession();
        await session.CreateVaultAsync(useHello: true);
        await session.LockAsync(LockReason.User);
        await session.UnlockWithHelloAsync(windowHandle: 0x1234);
        Assert.Equal((nint)0x1234, v.Consent.LastWindowHandle);
        try { Directory.Delete(v.Folder, true); } catch (IOException) { }
    }
}
