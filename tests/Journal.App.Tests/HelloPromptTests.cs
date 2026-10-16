using Journal.App.ViewModels;
using Journal.App.Views;
using Journal.Core.Security;
using Journal.Core.Tests.Support;

namespace Journal.App.Tests;

/// <summary>Windows Hello prompts start only when asked for and never outlive the unlock or setup window (ISS-007, ISS-008).</summary>
public class HelloPromptTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(3);

    private static async Task<(VaultSession Session, FakeKeyProtector Hello, RecoveryCode Code)> LockedVaultAsync()
    {
        var machine = new FakeMachine();
        var hello = new FakeKeyProtector(KeySlotTypes.WindowsHello) { Machine = machine };
        var session = new VaultSession(new InMemoryHeaderStore(), [hello, new FakeKeyProtector(KeySlotTypes.RecoveryCode)], machine, new FakeClock());
        var code = (await session.CreateVaultAsync(useHello: true)).RecoveryCode;
        await session.LockAsync(LockReason.User);
        return (session, hello, code);
    }

    private static async Task<bool> FinishesAsync(Task task) => await Task.WhenAny(task, Task.Delay(Patience)) == task;

    [Fact(DisplayName = "TC-SEC-22 UI tests never start the real app (no unlock window or Windows Hello on the PC)")]
    public void TestHostIsNotTheApp() => AppThread.Run(async () =>
    {
        await AppThread.Settle();
        Assert.IsNotType<Journal.App.App>(System.Windows.Application.Current);
        Assert.DoesNotContain(System.Windows.Application.Current.Windows.Cast<System.Windows.Window>(), w => w is UnlockWindow or SetupWindow);
    });

    [Fact(DisplayName = "TC-SEC-23 The unlock window waits for the button before asking Windows Hello")]
    public void NoAutomaticPrompt() => AppThread.Run(async () =>
    {
        var (session, hello, _) = await LockedVaultAsync();
        var vm = new UnlockViewModel(session, () => 0);
        var window = new UnlockWindow(vm) { Left = -20000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            await AppThread.Settle();
            await Task.Delay(300);
            await AppThread.Settle();
            Assert.Equal(0, hello.UnprotectCalls);
            Assert.True(vm.CanUseHello);
            await vm.TryHelloCommand.ExecuteAsync(null);
            Assert.Equal(1, hello.UnprotectCalls);
            Assert.True(session.IsUnlocked);
        }
        finally { window.Close(); }
    });

    [Fact(DisplayName = "TC-SEC-22 Cancelling a waiting Windows Hello request ends it and leaves the journal locked")]
    public void CancelPending() => Sta.Run(async () =>
    {
        var (session, hello, _) = await LockedVaultAsync();
        hello.WaitForUser = true;
        var vm = new UnlockViewModel(session, () => 0);
        await vm.InitializeAsync();
        var pending = vm.TryHelloCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        vm.CancelPendingHello();
        Assert.True(await FinishesAsync(pending), "the Windows Hello request was not cancelled");
        Assert.True(hello.SawCancellation);
        Assert.False(vm.IsBusy);
        Assert.False(session.IsUnlocked);
    });

    [Fact(DisplayName = "TC-SEC-22 The recovery code works while a Windows Hello request is waiting, and cancels it")]
    public void RecoveryWhileHelloWaits() => Sta.Run(async () =>
    {
        var (session, hello, code) = await LockedVaultAsync();
        hello.WaitForUser = true;
        var vm = new UnlockViewModel(session, () => 0);
        await vm.InitializeAsync();
        var pending = vm.TryHelloCommand.ExecuteAsync(null);
        vm.UseRecoveryCodeCommand.Execute(null);
        vm.RecoveryInput = code.Formatted;
        var submit = vm.SubmitRecoveryCodeCommand.ExecuteAsync(null);
        Assert.True(await FinishesAsync(submit), "recovery-code unlock did not finish");
        Assert.True(session.IsUnlocked);
        Assert.True(await FinishesAsync(pending), "the Windows Hello request was left waiting");
        Assert.True(hello.SawCancellation);
    });

    [Fact(DisplayName = "TC-SEC-22 Closing the unlock window cancels a waiting Windows Hello request")]
    public void CloseUnlockWindowCancels() => AppThread.Run(async () =>
    {
        var (session, hello, _) = await LockedVaultAsync();
        hello.WaitForUser = true;
        var vm = new UnlockViewModel(session, () => 0);
        var window = new UnlockWindow(vm) { Left = -20000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        await AppThread.Settle();
        var pending = vm.TryHelloCommand.ExecuteAsync(null);
        window.Close();
        Assert.True(await FinishesAsync(pending), "the Windows Hello request outlived the window");
        Assert.True(hello.SawCancellation);
    });

    [Fact(DisplayName = "TC-SEC-22 Closing the setup window cancels a waiting Windows Hello request")]
    public void CloseSetupWindowCancels() => AppThread.Run(async () =>
    {
        var machine = new FakeMachine();
        var hello = new FakeKeyProtector(KeySlotTypes.WindowsHello) { Machine = machine, WaitForUser = true };
        var session = new VaultSession(new InMemoryHeaderStore(), [hello, new FakeKeyProtector(KeySlotTypes.RecoveryCode)], machine, new FakeClock());
        var folder = Path.Combine(Path.GetTempPath(), "journal-app-tests", Guid.NewGuid().ToString("N"));
        var vm = new SetupViewModel(_ => session, () => Task.FromResult(true), new FakeDialogs(), () => 0, folder, null);
        var window = new SetupWindow(vm) { Left = -20000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        await AppThread.Settle();
        var creating = vm.CreateCommand.ExecuteAsync(null);
        window.Close();
        Assert.True(await FinishesAsync(creating), "the Windows Hello request outlived the setup window");
        Assert.True(hello.SawCancellation);
        try { Directory.Delete(folder, true); } catch (IOException) { }
    });
}
