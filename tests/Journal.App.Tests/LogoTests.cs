using System.Windows;
using System.Windows.Controls;
using Journal.App.ViewModels;
using Journal.App.Views;
using Journal.Core.Security;
using Journal.Core.Settings;
using Journal.Core.Tests.Support;

namespace Journal.App.Tests;

public class LogoTests
{
    private static readonly Uri IconUri = new("pack://application:,,,/Journal;component/Assets/Journal.ico");

    /// <summary>Reads the image sizes listed in an .ico file's directory.</summary>
    private static IReadOnlyList<int> IconSizes(Stream ico)
    {
        using var r = new BinaryReader(ico);
        r.ReadUInt16(); // reserved
        Assert.Equal(1, r.ReadUInt16()); // type: icon
        int count = r.ReadUInt16();
        var sizes = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int w = r.ReadByte();
            r.ReadBytes(15);
            sizes.Add(w == 0 ? 256 : w);
        }
        return sizes;
    }

    private static VaultSession Session()
    {
        var machine = new FakeMachine();
        return new VaultSession(new InMemoryHeaderStore(), [new FakeKeyProtector(KeySlotTypes.RecoveryCode)], machine, new FakeClock());
    }

    [Fact(DisplayName = "TC-UI-01 The icon has all sizes, every window shows it, and the quill logo is in the app")]
    public void QuillLogo() => AppThread.Run(async () =>
    {
        // Multi-size icon resource for Explorer, Start menu, taskbar and title bars.
        var info = Application.GetResourceStream(IconUri);
        Assert.NotNull(info);
        Assert.Equal([16, 24, 32, 48, 64, 256], IconSizes(info!.Stream).Order());

        // Every app window uses it.
        var h = new AppHarness();
        var main = new MainWindow(new MainViewModel(h.Context));
        var unlock = new UnlockWindow(new UnlockViewModel(Session(), () => 0));
        var setup = new SetupWindow(new SetupViewModel(_ => Session(), () => Task.FromResult(true), h.Dialogs, () => 0, @"C:\j", null));
        var settings = new SettingsWindow(new SettingsViewModel(new MinimalSettingsHost(Session()), h.Dialogs, new AppSettings()));
        foreach (Window w in new Window[] { main, unlock, setup, settings })
            Assert.True(w.Icon is not null, $"{w.GetType().Name} has no icon");

        // The quill logo is shown in the main window's top bar and on the unlock and setup screens.
        foreach (Window w in new Window[] { main, unlock, setup })
        {
            var logo = w.FindName("Logo") as Image;
            Assert.True(logo?.Source is not null, $"{w.GetType().Name} doesn't show the logo");
        }
        foreach (Window w in new Window[] { main, unlock, setup, settings }) w.Close();
        await Task.CompletedTask;
    });

    private sealed class MinimalSettingsHost(VaultSession session) : ISettingsHost
    {
        public VaultSession Session => session;
        public string VaultFolder => @"C:\vault";
        public IReadOnlyList<string> AvailableBooks => [];
        public IReadOnlyList<string> AvailableTraditions => [];
        public int ImportedVerseCount => 0;
        public int ImportedQuoteCount => 0;
        public Task<string> ImportVersesAsync(string path) => Task.FromResult("");
        public Task<string> ImportQuotesAsync(string path) => Task.FromResult("");
        public Task MoveVaultAsync(string newFolder) => Task.CompletedTask;
        public void ExportBackup(string zipPath) { }
        public Task ApplySettingsAsync(AppSettings settings) => Task.CompletedTask;
    }
}
