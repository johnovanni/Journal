using System.Windows;
using System.Windows.Controls;
using Journal.App.Controls;
using Journal.App.ViewModels;
using Journal.App.Views;
using Journal.Core.Security;
using Journal.Core.Settings;
using Journal.Core.Tests.Support;
using Journal.Core.Tracking;

namespace Journal.App.Tests;

/// <summary>ISS-030: tracked values on the main screen, in the Graphs window and in Settings.</summary>
public class TrackingViewModelTests
{
    private static readonly Measure Weight = new("Weight", "lb", MeasurePeriod.Weekly, true);
    private static readonly Measure Pressure = new("Blood pressure", "mmHg", MeasurePeriod.Monthly, true);
    private static readonly Measure Sleep = new("Sleep", "h", MeasurePeriod.Weekly, false);

    /// <summary>A journal with a few weeks of weight, two blood pressure readings and one sleep value.</summary>
    private static async Task<AppHarness> HarnessAsync(bool on = true)
    {
        var h = new AppHarness();
        h.Settings.TrackValues = on;
        var today = TestData.Today;   // Thursday 2026-10-15
        await h.Journal.SaveAsync(TestData.Entry(today.AddDays(-21), "##weight=190"));
        await h.Journal.SaveAsync(TestData.Entry(today.AddDays(-14), "##weight=188 ##blood pressure=128/84"));
        await h.Journal.SaveAsync(TestData.Entry(today.AddDays(-7), "##weight=186.5 ##sleep=7"));
        await h.Journal.SaveAsync(TestData.Entry(today, "##blood pressure=121/79"));
        await h.Journal.Tracking.SaveMeasuresAsync([Weight, Pressure, Sleep]);
        return h;
    }

    [Fact(DisplayName = "TC-TRK-06 The main screen shows only measures ticked for it, at most two, and nothing when the feature is off")]
    public void MainScreenCards() => Sta.Run(async () =>
    {
        var h = await HarnessAsync();
        var vm = await h.MainAsync();
        var cards = vm.Tracked.Cards;
        Assert.True(vm.Tracked.IsEnabled);
        Assert.Equal(["Weight", "Blood pressure"], cards.Select(c => c.Name));
        Assert.Equal("186.5 lb", cards[0].LatestText);
        Assert.Equal("121/79 mmHg", cards[1].LatestText);
        Assert.Equal(3, cards[0].Points.Count);
        Assert.Equal(121, cards[1].Points[^1].First);
        Assert.Equal(79, cards[1].Points[^1].Second);
        Assert.Contains("Weight: 186.5 lb", cards[0].AutomationName);

        // A new value shows at once.
        await h.Journal.SaveAsync(TestData.Entry(TestData.Today.AddDays(-1), "##weight=185"));
        Assert.Equal("185 lb", vm.Tracked.Cards[0].LatestText);

        // Changing which measures are shown, or turning the feature off, updates the cards.
        await h.Journal.Tracking.SaveMeasuresAsync([Weight with { ShowOnMain = false }, Pressure, Sleep]);
        Assert.Equal(["Blood pressure"], vm.Tracked.Cards.Select(c => c.Name));
        h.Settings.TrackValues = false;
        vm.SettingsApplied(h.Settings);
        Assert.False(vm.Tracked.IsEnabled);
        Assert.Empty(vm.Tracked.Cards);
        Assert.False(vm.Tracked.ShowCards);

        // Clicking a card asks for the graphs at that measure.
        h.Settings.TrackValues = true;
        vm.SettingsApplied(h.Settings);
        Measure? asked = null;
        vm.GraphsRequested += (_, m) => asked = m;
        vm.Tracked.OpenGraphsCommand.Execute(vm.Tracked.Cards[0]);
        Assert.Equal("Blood pressure", asked?.Name);
    });

    [Fact(DisplayName = "TC-TRK-07 The Graphs window lists every measure, changes range, opens a day from a point and lists the values")]
    public void Graphs() => AppThread.Run(async () =>
    {
        var h = await HarnessAsync();
        var vm = new MainViewModel(h.Context);
        var window = new MainWindow(vm) { Width = 1240, Height = 820, Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show();
        await vm.InitializeAsync();
        try
        {
            var graphsWindow = window.ShowGraphs(Pressure);
            await AppThread.Settle();
            var g = graphsWindow.Vm;
            Assert.Equal(["Weight", "Blood pressure", "Sleep"], g.Graphs.Select(x => x.Name));
            Assert.Equal("1 year", g.Range.Label);
            var weight = g.Graphs[0];
            Assert.Equal(["Week of Oct 4, 2026", "Week of Sep 27, 2026", "Week of Sep 20, 2026"], weight.Rows.Select(r => r.PeriodText));   // newest first
            Assert.Equal("Latest: 186.5 lb", weight.LatestText);
            Assert.Contains("two values", g.Graphs[1].Details);
            Assert.All(g.Graphs, x => Assert.False(string.IsNullOrWhiteSpace(x.AutomationName)));

            // Three months back from Oct 15 starts at Jul 16: everything still shows; a value in June doesn't.
            await h.Journal.SaveAsync(TestData.Entry(new(2026, 6, 1), "##weight=200"));
            Assert.Equal(4, g.Graphs[0].Points.Count);
            g.Range = g.Ranges[0];
            Assert.Equal(3, g.Graphs[0].Points.Count);
            g.Range = g.Ranges[2];
            Assert.Equal(4, g.Graphs[0].Points.Count);

            // Clicking a point or a row's date opens that day in the main window.
            await g.OpenDayCommand.ExecuteAsync(g.Graphs[0].Points[1]);
            Assert.Equal(TestData.Today.AddDays(-21), vm.CurrentEntry!.Date);
            await g.OpenDayCommand.ExecuteAsync(g.Graphs[1].Rows[0]);
            Assert.Equal(TestData.Today, vm.CurrentEntry!.Date);

            // The window draws a graph per measure, each with a screen-reader name, and a table of values.
            await AppThread.Settle();
            var charts = AppThread.FindChildren<LineChart>(graphsWindow).ToList();
            Assert.Equal(3, charts.Count);
            Assert.All(charts, c => Assert.StartsWith("Graph of", System.Windows.Automation.AutomationProperties.GetName(c)));
            Assert.Equal(3, AppThread.FindChildren<Expander>(graphsWindow).Count());

            // Opening again brings the same window forward; it closes with the main window (as when locking).
            Assert.Same(graphsWindow, window.ShowGraphs(Weight));
            bool closed = false;
            graphsWindow.Closed += (_, _) => closed = true;
            window.Close();
            await AppThread.Settle();
            Assert.True(closed);
        }
        finally { if (window.IsLoaded) window.Close(); }
    });

    private static FakeSettingsHost SettingsHost() =>
        new(new VaultSession(new InMemoryHeaderStore(), [new FakeKeyProtector(KeySlotTypes.RecoveryCode)], new FakeMachine(), new FakeClock()))
        {
            Measures = [Weight, Pressure],
        };

    [Fact(DisplayName = "TC-TRK-02 Settings: add, edit and remove tracked values; at most two on the main screen; a bad name saves nothing")]
    public void SettingsMeasures() => Sta.Run(async () =>
    {
        var host = SettingsHost();
        var dialogs = new FakeDialogs();
        var vm = new SettingsViewModel(host, dialogs, new AppSettings());
        Assert.False(vm.TrackValues);   // off by default
        Assert.Equal(["Weight", "Blood pressure"], vm.Measures.Select(m => m.Name));

        vm.AddMeasureCommand.Execute(null);
        var sleep = vm.Measures[^1];
        sleep.Name = "Sleep";
        sleep.Unit = "h";
        sleep.ShowOnMain = true;   // a third: refused
        Assert.False(sleep.ShowOnMain);
        Assert.Contains("At most 2", vm.Status);

        vm.Measures[0].ShowOnMain = false;
        sleep.ShowOnMain = true;
        Assert.True(sleep.ShowOnMain);

        // An empty name or two of the same name: an error, and nothing is saved.
        vm.AddMeasureCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Single(dialogs.Errors);
        Assert.Equal(0, host.MeasureSaves);
        Assert.Null(host.Applied);
        vm.Measures[^1].Name = "sleep ";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(2, dialogs.Errors.Count);
        vm.RemoveMeasureCommand.Execute(vm.Measures[^1]);

        vm.TrackValues = true;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, host.MeasureSaves);
        Assert.Equal([new Measure("Weight", "lb", MeasurePeriod.Weekly, false), Pressure, new Measure("Sleep", "h", MeasurePeriod.Weekly, true)], host.Measures);
        Assert.True(host.Applied!.TrackValues);

        // Saving again without changes doesn't rewrite the measures.
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, host.MeasureSaves);
    });

    [Fact(DisplayName = "TC-TRK-08 The tracked-value card fits the left column: graphs at the default size, one line on a shorter window, hidden at 1000×650 and while searching")]
    public void CardFitsColumn() => AppThread.Run(async () =>
    {
        var h = await HarnessAsync();
        foreach (var (height, expectShown, expectCompact) in new[] { (820.0, true, false), (750.0, true, true), (650.0, false, true) })
        {
            var vm = new MainViewModel(h.Context);
            var window = new MainWindow(vm) { Width = 1000, Height = height, Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
            window.Show();
            await vm.InitializeAsync();
            await AppThread.Settle();
            try
            {
                var content = (FrameworkElement)window.Content;
                var card = (FrameworkElement)window.FindName("TrackedCard");
                var search = (FrameworkElement)window.FindName("SearchBox");
                Assert.Equal(expectShown, card.IsVisible);
                Assert.Equal(expectCompact, vm.Tracked.IsCompact);
                var sparks = AppThread.FindChildren<LineChart>(card).Where(c => c.IsVisible).Count();
                Assert.Equal(expectShown && !expectCompact ? 2 : 0, sparks);
                // The search box stays whole and inside the window.
                var bottom = search.TranslatePoint(new Point(0, search.ActualHeight), content).Y;
                Assert.True(bottom <= content.ActualHeight - 20, $"search box ends at {bottom:0} of {content.ActualHeight:0} at height {height}");
                if (expectShown)
                {
                    var cardBottom = card.TranslatePoint(new Point(0, card.ActualHeight), content).Y;
                    Assert.True(cardBottom < search.TranslatePoint(new Point(0, 0), content).Y, "the card overlaps the search box");
                }

                // Searching hides the card so the results have the column.
                vm.Search.QueryText = "weight";
                await AppThread.Settle();
                Assert.False(card.IsVisible);
                vm.Search.QueryText = "";
                await AppThread.Settle();
                Assert.Equal(expectShown, card.IsVisible);

                // The Graphs button shows only while the feature is on.
                var graphsButton = AppThread.FindChild<Button>(window, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Graphs")!;
                Assert.True(graphsButton.IsVisible);
            }
            finally { window.Close(); }
        }

        h.Settings.TrackValues = false;
        var offVm = new MainViewModel(h.Context);
        var off = new MainWindow(offVm) { Width = 1240, Height = 820, Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        off.Show();
        await offVm.InitializeAsync();
        await AppThread.Settle();
        try
        {
            Assert.False(((FrameworkElement)off.FindName("TrackedCard")).IsVisible);
            Assert.False(AppThread.FindChild<Button>(off, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Graphs")!.IsVisible);
        }
        finally { off.Close(); }
    });
}
