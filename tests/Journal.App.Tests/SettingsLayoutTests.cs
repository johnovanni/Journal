using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Journal.App.ViewModels;
using Journal.App.Views;
using Journal.Core.Inspiration;
using Journal.Core.Security;
using Journal.Core.Settings;
using Journal.Core.Tests.Support;

namespace Journal.App.Tests;

/// <summary>ISS-015: the real Settings window lays out its tick-box lists down columns.</summary>
public class SettingsLayoutTests
{
    private static readonly string[] Books =
        ["Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy", "Joshua", "Judges", "Ruth", "1 Samuel", "2 Samuel", "Psalms", "Proverbs", "Isaiah", "Matthew"];

    private static readonly string[] PhilosophyTraditions =
        ["Stoic", "Christian", "Chinese", "Renaissance", "Roman", "Buddhist", "Modern European", "Greek", "Transcendentalist", "Enlightenment", "French Moralist"];

    [Fact(DisplayName = "TC-INSP-05 Settings lists run down columns: traditions alphabetical, books in Bible order")]
    public void ColumnLayout() => AppThread.Run(async () =>
    {
        var host = new Host(new VaultSession(new InMemoryHeaderStore(), [new FakeKeyProtector(KeySlotTypes.RecoveryCode)], new FakeMachine(), new FakeClock()));
        var window = new SettingsWindow(new SettingsViewModel(host, new FakeDialogs(), new AppSettings()))
        {
            Left = -20000, ShowActivated = false, ShowInTaskbar = false,
        };
        window.Show();
        var tabs = Descendants<TabControl>(window).Single();
        tabs.SelectedIndex = tabs.Items.Cast<TabItem>().ToList().FindIndex(t => (string)t.Header == "Verse & quote");
        window.UpdateLayout();

        // Finds a tick box inside the list with the given accessible name ("Chinese" is in two lists).
        Func<string, CheckBox> In(DependencyObject root) => name =>
            Descendants<CheckBox>(root).Single(c => c.Content as string == name);
        ItemsControl List(string name) =>
            Descendants<ItemsControl>(window).Single(l => System.Windows.Automation.AutomationProperties.GetName(l) == name);

        // Philosophy's section comes before Literature's.
        Assert.True(Top(In(window)("Include philosophy quotes"), window) < Top(In(window)("Include literature quotes"), window));

        AssertDownColumns([.. PhilosophyTraditions.Order(StringComparer.CurrentCultureIgnoreCase)], In(List("PHILOSOPHY traditions")), window);
        AssertDownColumns(["Chinese", "English", "Persian"], In(List("LITERATURE traditions")), window);
        AssertDownColumns(Books, In(List("Books")), window);
        window.Close();
        await Task.CompletedTask;
    });

    /// <summary>
    /// Items fill the first column top to bottom, then the next, in the given order; columns line up;
    /// and no tick box is cut off.
    /// </summary>
    private static void AssertDownColumns(string[] order, Func<string, CheckBox> box, Window window)
    {
        int rows = (order.Length + 2) / 3;
        for (int i = 0; i < order.Length; i++)
        {
            var b = box(order[i]);
            Assert.True(b.ActualWidth >= b.DesiredSize.Width - 0.5, $"'{order[i]}' is cut off");
            if (i % rows != 0)
            {
                var above = box(order[i - 1]);
                Assert.True(Top(b, window) > Top(above, window), $"'{order[i]}' should be below '{order[i - 1]}'");
                Assert.Equal(Left(above, window), Left(b, window), 1);
            }
            else if (i > 0)
            {
                var previousColumn = box(order[i - rows]);
                Assert.True(Left(b, window) > Left(previousColumn, window), $"'{order[i]}' should start a new column");
                Assert.Equal(Top(previousColumn, window), Top(b, window), 1);
            }
        }
    }

    private static double Top(FrameworkElement e, Window w) => e.TransformToAncestor(w).Transform(new Point()).Y;
    private static double Left(FrameworkElement e, Window w) => e.TransformToAncestor(w).Transform(new Point()).X;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    private sealed class Host(VaultSession session) : ISettingsHost
    {
        public VaultSession Session => session;
        public string VaultFolder => @"C:\vault";
        public IReadOnlyList<string> AvailableBooks => Books;
        public IReadOnlyList<QuoteCategory> QuoteCategories =>
            [new("Literature", ["English", "Persian", "Chinese"]), new("Philosophy", PhilosophyTraditions)];
        public int ImportedVerseCount => 0;
        public int ImportedQuoteCount => 0;
        public Task<string> ImportVersesAsync(string path) => Task.FromResult("");
        public Task<string> ImportQuotesAsync(string path) => Task.FromResult("");
        public Task MoveVaultAsync(string newFolder) => Task.CompletedTask;
        public void ExportBackup(string zipPath) { }
        public Task ApplySettingsAsync(AppSettings settings) => Task.CompletedTask;
    }
}
