using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Journal.App.Services;
using Journal.App.ViewModels;
using Journal.App.Views;
using Journal.Core.Security;
using Journal.Core.Settings;
using Journal.Core.Tests.Support;

namespace Journal.App.Tests;

/// <summary>ISS-017: the app shows the version built into the program.</summary>
public class VersionTests
{
    private static VaultSession Session() =>
        new(new InMemoryHeaderStore(), [new FakeKeyProtector(KeySlotTypes.RecoveryCode)], new FakeMachine(), new FakeClock());

    [Fact(DisplayName = "TC-UI-03 The version comes from the build, without the commit suffix")]
    public void FromBuild()
    {
        var built = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version;
        Assert.DoesNotContain("+", AppInfo.Version);
        Assert.StartsWith(AppInfo.Version + ".", built + ".");   // file version is 1.1.0.0, shown as 1.1.0
        Assert.Equal("1.1.0", AppInfo.Clean("1.1.0+b7c09fff204c6f333fa091a6b97f5031186e4861"));
        Assert.Equal("1.1.0", AppInfo.Clean("1.1.0"));
    }

    [Fact(DisplayName = "TC-UI-03 Settings and the unlock window show the version")]
    public void Shown() => AppThread.Run(async () =>
    {
        var expected = $"Journal {AppInfo.Version}";
        var settings = new SettingsWindow(new SettingsViewModel(new FakeSettingsHost(Session()), new FakeDialogs(), new AppSettings()))
        {
            Left = -20000, ShowActivated = false, ShowInTaskbar = false,
        };
        var unlock = new UnlockWindow(new UnlockViewModel(Session(), () => 0))
        {
            Left = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
        };
        settings.Show();
        unlock.Show();
        await AppThread.Settle();
        foreach (Window w in new Window[] { settings, unlock })
        {
            var label = Assert.IsType<TextBlock>(w.FindName("VersionLabel"));
            Assert.Equal(expected, label.Text);
            Assert.True(label.IsVisible, $"{w.GetType().Name} hides the version");
            w.Close();
        }
    });
}
