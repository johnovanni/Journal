using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Journal.App.ViewModels;
using Journal.App.Views;
using Journal.Core.Domain;
using Journal.Core.Inspiration;
using Journal.Core.Security;
using Journal.Core.Settings;
using Journal.Core.Tests.Support;

namespace Journal.App.Tests;

/// <summary>ISS-012: a Settings section per verse tradition, and the scripture as the verse card's label.</summary>
public class ScriptureSettingsTests
{
    private static FakeSettingsHost Host() =>
        new(new VaultSession(new InMemoryHeaderStore(), [new FakeKeyProtector(KeySlotTypes.RecoveryCode)], new FakeMachine(), new FakeClock()))
        {
            Traditions =
            [
                new("Christianity", ["Bible"], ["Psalms", "Proverbs"]),
                new("Islam", ["Quran"], ["Quran"]),
                new("Taoism", ["Tao Te Ching", "Chuang Tzu"], ["Tao Te Ching", "Chuang Tzu"]),
            ],
        };

    [Fact(DisplayName = "TC-INSP-19 Settings: a section per tradition, only Christianity ticked at first; choices are saved")]
    public void Sections() => Sta.Run(async () =>
    {
        var host = Host();
        var vm = new SettingsViewModel(host, new FakeDialogs(), new AppSettings());
        Assert.Equal(["Christianity", "Islam", "Taoism"], vm.VerseSections.Select(s => s.Name));
        Assert.Equal([true, false, false], vm.VerseSections.Select(s => s.IsIncluded));
        Assert.Equal("Include Taoism: Tao Te Ching, Chuang Tzu", vm.VerseSections[2].IncludeLabel);
        Assert.False(vm.VerseSections[1].HasBooks);   // a single book needs no list
        Assert.True(vm.VerseSections[2].HasBooks);
        Assert.Equal(["Psalms", "Proverbs"], vm.Books.Select(b => b.Name));

        vm.VerseSections[2].IsIncluded = true;
        vm.VerseSections[2].Books.Single(b => b.Name == "Chuang Tzu").IsChecked = true;
        vm.Books.Single(b => b.Name == "Psalms").IsChecked = true;
        await vm.SaveCommand.ExecuteAsync(null);
        var saved = host.Applied!.Inspiration;
        Assert.Equal(["Christianity", "Taoism"], saved.VerseTraditions);
        Assert.Equal(["Psalms", "Taoism/Chuang Tzu"], saved.VerseBooks);   // a Bible book keeps its plain name

        // Opening Settings again shows the same choices.
        var again = new SettingsViewModel(host, new FakeDialogs(), host.Applied);
        Assert.Equal([true, false, true], again.VerseSections.Select(s => s.IsIncluded));
        Assert.True(again.VerseSections[2].Books.Single(b => b.Name == "Chuang Tzu").IsChecked);
        Assert.False(again.VerseSections[2].Books.Single(b => b.Name == "Tao Te Ching").IsChecked);
        Assert.True(again.Books.Single(b => b.Name == "Psalms").IsChecked);
    });

    [Fact(DisplayName = "TC-INSP-19 The Settings window shows each tradition's tick box, and a book list only where there are books to choose")]
    public void Window() => AppThread.Run(async () =>
    {
        var window = new SettingsWindow(new SettingsViewModel(Host(), new FakeDialogs(), new AppSettings()))
        {
            Left = -20000, ShowActivated = false, ShowInTaskbar = false,
        };
        window.Show();
        var tabs = Descendants<TabControl>(window).Single();
        tabs.SelectedIndex = tabs.Items.Cast<TabItem>().ToList().FindIndex(t => (string)t.Header == "Verse & quote");
        window.UpdateLayout();
        CheckBox Box(string label) => Descendants<CheckBox>(window).Single(c => c.Content as string == label);
        Assert.True(Box("Include Christianity: Bible").IsChecked);
        Assert.False(Box("Include Islam: Quran").IsChecked);
        Assert.True(Box("Chuang Tzu").IsVisible);
        Assert.DoesNotContain(Descendants<CheckBox>(window), c => c.Content as string == "Quran" && c.IsVisible);
        Assert.Contains(Descendants<CheckBox>(window), c => c.Content as string == "Show a verse each day");
        window.Close();
        await Task.CompletedTask;
    });

    [Fact(DisplayName = "TC-INSP-18 The verse card is labelled with the scripture; a day saved before shows BIBLE")]
    public void CardLabel()
    {
        var vm = new InspirationViewModel();
        vm.Update(new DailyInspiration(new Verse("Quran 2:255", "Allah! There is no deity save Him.", "Pickthall") { Tradition = "Islam", Scripture = "Quran" }, null));
        Assert.Equal("QURAN", vm.VerseLabel);
        Assert.Equal("Quran 2:255 · Pickthall", vm.VerseReference);
        vm.Update(new DailyInspiration(new Verse("John 3:16", "For God so loved the world.", "WEB"), null));
        Assert.Equal("BIBLE", vm.VerseLabel);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }
}
