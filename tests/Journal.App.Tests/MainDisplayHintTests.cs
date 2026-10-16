using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Journal.App.Services;
using Journal.App.ViewModels;
using Journal.App.Views;
using Journal.Core.Security;
using Journal.Core.Tests.Support;

namespace Journal.App.Tests;

/// <summary>ISS-009: Windows shows the key-signing dialog on the main display, so the app says so when it's elsewhere.</summary>
public class MainDisplayHintTests
{
    private static async Task<(VaultSession Session, FakeMachine Machine)> LockedVaultAsync()
    {
        var machine = new FakeMachine();
        var hello = new FakeKeyProtector(KeySlotTypes.WindowsHello) { Machine = machine };
        var session = new VaultSession(new InMemoryHeaderStore(), [hello, new FakeKeyProtector(KeySlotTypes.RecoveryCode)], machine, new FakeClock());
        await session.CreateVaultAsync(useHello: true);
        await session.LockAsync(LockReason.User);
        return (session, machine);
    }

    [Fact(DisplayName = "TC-SEC-25 Unlock hint shows only when Windows Hello is offered and the window is on a side display")]
    public void UnlockHintLogic() => Sta.Run(async () =>
    {
        var (session, machine) = await LockedVaultAsync();
        var vm = new UnlockViewModel(session, () => 0);
        await vm.InitializeAsync();
        vm.IsOnPrimaryDisplay = false;
        Assert.True(vm.ShowMainDisplayHint);
        vm.IsOnPrimaryDisplay = true;
        Assert.False(vm.ShowMainDisplayHint);

        machine.MachineName = "OTHER-PC"; // no Windows Hello for this PC: nothing to hint about
        var other = new UnlockViewModel(session, () => 0);
        await other.InitializeAsync();
        other.IsOnPrimaryDisplay = false;
        Assert.False(other.ShowMainDisplayHint);
    });

    [Fact(DisplayName = "TC-SEC-25 Setup hint shows only when Windows Hello is available and the window is on a side display")]
    public void SetupHintLogic() => Sta.Run(async () =>
    {
        var (session, _) = await LockedVaultAsync();
        var vm = new SetupViewModel(_ => session, () => Task.FromResult(true), new FakeDialogs(), () => 0, @"C:\j", null);
        await vm.InitializeAsync();
        vm.IsOnPrimaryDisplay = false;
        Assert.True(vm.ShowMainDisplayHint);
        vm.IsOnPrimaryDisplay = true;
        Assert.False(vm.ShowMainDisplayHint);
        var noHello = new SetupViewModel(_ => session, () => Task.FromResult(false), new FakeDialogs(), () => 0, @"C:\j", null);
        await noHello.InitializeAsync();
        noHello.IsOnPrimaryDisplay = false;
        Assert.False(noHello.ShowMainDisplayHint);
    });

    [Fact(DisplayName = "TC-SEC-25 The real unlock window shows the hint on a side display and hides it on the main one")]
    public void UnlockWindowFollowsDisplay() => AppThread.Run(async () =>
    {
        var (session, _) = await LockedVaultAsync();
        var vm = new UnlockViewModel(session, () => 0);
        var window = new UnlockWindow(vm) { WindowStartupLocation = WindowStartupLocation.Manual, Left = 100, Top = 100, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            await AppThread.Settle();
            await Task.Delay(200);
            await AppThread.Settle();
            var hint = (TextBlock)window.FindName("MainDisplayHint");
            Assert.True(DisplayInfo.IsOnPrimaryDisplay(new WindowInteropHelper(window).Handle));
            Assert.False(hint.IsVisible, "hint shown on the main display");

            // Move to a side display when this PC has one (left or right of the primary display).
            var side = SideDisplayPoint();
            if (side is null) return;
            window.Left = side.Value.X;
            window.Top = side.Value.Y;
            await AppThread.Settle();
            Assert.False(DisplayInfo.IsOnPrimaryDisplay(new WindowInteropHelper(window).Handle));
            Assert.True(hint.IsVisible, "hint missing on a side display");
            Assert.Equal(UnlockViewModel.MainDisplayHint, hint.Text);
        }
        finally { window.Close(); }
    });

    /// <summary>A point (in WPF units) well inside a non-primary display, or null on a single-display PC.</summary>
    private static Point? SideDisplayPoint()
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var primaryWidth = SystemParameters.PrimaryScreenWidth;
        if (virtualWidth <= primaryWidth + 1) return null;
        // Displays can have gaps between them, so aim well inside the far right (or far left) of the desktop.
        var right = virtualLeft + virtualWidth;
        if (right > primaryWidth + 200) return new Point(right - 900, 150);
        return new Point(virtualLeft + 200, 150);
    }
}
