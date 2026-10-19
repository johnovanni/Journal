using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Journal.App.Controls;
using Journal.App.ViewModels;
using Journal.App.Views;
using Journal.Core.Domain;
using Journal.Core.Tests.Support;
using Journal.Infrastructure.Content;

namespace Journal.App.Tests;

/// <summary>
/// Hosts the real WPF Application (with the app's theme resources) on one STA thread for tests that
/// create real windows. All such tests run on this thread, one at a time.
/// </summary>
/// <summary>Creates a plain WPF Application that carries Journal's theme and resources, without the app's startup.</summary>
public static class UiHost
{
    public static Application CreateApplication()
    {
        Application.ResourceAssembly ??= typeof(Journal.App.App).Assembly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var path in new[] { "Assets/Quill.xaml", "Themes/Theme.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Journal;component/{path}"),
            });
        return app;
    }
}

public static class AppThread
{
    private static readonly Lazy<Dispatcher> Host = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>();
        var thread = new Thread(() =>
        {
            // Never create Journal.App.App: WPF runs an Application's startup as soon as its dispatcher runs,
            // which would start the real app (unlock window, Windows Hello) on the user's PC (ISS-007).
            // A plain Application with the app's resources is enough for real windows.
            UiHost.CreateApplication();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.Result;
    });

    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static void Run(Func<Task> test)
    {
        Gate.Wait();
        try
        {
            var done = new TaskCompletionSource();
            Host.Value.BeginInvoke(async () =>
            {
                try { await test(); done.SetResult(); }
                catch (Exception e) { done.SetException(e); }
            });
            try { done.Task.Wait(TimeSpan.FromSeconds(60)); }
            catch (AggregateException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); }
        }
        finally { Gate.Release(); }
    }

    /// <summary>Lets layout, data binding and loaded events run.</summary>
    public static async Task Settle()
    {
        for (int i = 0; i < 3; i++)
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    public static T? FindChild<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && (match is null || match(t))) return t;
            if (FindChild(child, match) is { } found) return found;
        }
        return null;
    }

    public static IEnumerable<T> FindChildren<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && (match is null || match(t))) yield return t;
            foreach (var found in FindChildren(child, match)) yield return found;
        }
    }
}

public class LayoutTests
{
    private const double MinEditorHeight = 100;   // about five lines of text
    private const double MaxCardHeight = 230;     // label + about seven lines of verse + reference row + padding

    /// <summary>Opens the real main window with the longest built-in verse and quote on today's entry.</summary>
    private static async Task<(MainWindow Window, FrameworkElement Content)> OpenLongestAsync(double width, double height)
    {
        var h = new AppHarness();
        var verse = new EmbeddedVerseSource().All().MaxBy(v => v.Text.Length)!;
        var quote = new EmbeddedQuoteSource().All().MaxBy(q => q.Text.Length)!;
        var entry = TestData.Entry(TestData.Today, "Spent the morning in the garden.");
        entry.Inspiration = new DailyInspiration(verse, quote);
        await h.Journal.SaveAsync(entry);
        var vm = new MainViewModel(h.Context);
        var window = new MainWindow(vm)
        {
            Width = width, Height = height, Left = -20000, Top = 0,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.Show();
        await vm.InitializeAsync();
        await AppThread.Settle();
        window.UpdateLayout();
        return (window, (FrameworkElement)window.Content);
    }

    private static double Bottom(FrameworkElement element, FrameworkElement root) =>
        element.TranslatePoint(new Point(0, element.ActualHeight), root).Y;

    [Fact(DisplayName = "TC-NFR-24 At 1000×650 with the longest verse and quote, the editor stays visible and usable")]
    public void EditorVisibleAtMinimumSize() => AppThread.Run(async () =>
    {
        var (window, content) = await OpenLongestAsync(1000, 650);
        try
        {
            var editor = AppThread.FindChild<JournalEditor>(window);
            Assert.NotNull(editor);
            var box = editor!.TextBox;
            Assert.True(box.ActualHeight >= MinEditorHeight, $"editor text area is {box.ActualHeight:0} px tall");
            Assert.True(Bottom(box, content) <= content.ActualHeight + 1, "editor extends below the window");
        }
        finally { window.Close(); }
    });

    [Fact(DisplayName = "TC-INSP-15 The verse and quote card is capped and its text scrolls")]
    public void CardHeightCapped() => AppThread.Run(async () =>
    {
        var (window, _) = await OpenLongestAsync(1240, 820);
        try
        {
            var card = window.FindName("InspirationCard") as FrameworkElement;
            Assert.NotNull(card);
            Assert.True(card!.ActualHeight <= MaxCardHeight, $"card is {card.ActualHeight:0} px tall");
            var verseScroller = AppThread.FindChild<ScrollViewer>(card, s => s.Name == "VerseScroller");
            Assert.NotNull(verseScroller);
            Assert.True(verseScroller!.ScrollableHeight > 0, "the longest verse should scroll inside the card");
            var copyVerse = AppThread.FindChild<Button>(card, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Copy verse");
            Assert.True(copyVerse is { IsVisible: true }, "the Copy button stays visible");
        }
        finally { window.Close(); }
    });

    [Fact(DisplayName = "TC-MOOD-06 Face buttons fit beside the heading and the counts show under the calendar, at the smallest window size")]
    public void MoodLayout() => AppThread.Run(async () =>
    {
        var (window, content) = await OpenLongestAsync(1000, 650);
        try
        {
            var vm = (MainViewModel)window.DataContext;
            var entry = vm.CurrentEntry!;
            await entry.ChooseMoodCommand.ExecuteAsync(entry.MoodChoices.Single(c => c.Mood == Mood.Happy));
            await AppThread.Settle();
            window.UpdateLayout();

            // The three face buttons are visible, on one line, to the right of the heading and clear of it.
            var heading = AppThread.FindChild<TextBlock>(window, t => t.Text == entry.Heading)!;
            var faces = AppThread.FindChildren<Button>(window, b => b.Style == window.FindResource("MoodButton")).ToList();
            Assert.Equal(3, faces.Count);
            var headingRight = heading.TranslatePoint(new Point(heading.ActualWidth, 0), content).X;
            foreach (var f in faces)
            {
                Assert.True(f.IsVisible && f.ActualWidth > 0);
                Assert.True(f.TranslatePoint(new Point(), content).X >= headingRight, "a face button overlaps the heading");
            }

            // The counts card shows the happy day.
            var counts = AppThread.FindChildren<MoodCount>(window).ToList();
            Assert.Equal(6, counts.Count);
            Assert.Equal("1", counts.Single(c => c.Mood == Mood.Happy && Grid.GetRow(c) == 1).Text);
            Assert.All(counts, c => Assert.True(c.IsVisible));
        }
        finally { window.Close(); }
    });

    [Fact(DisplayName = "TC-EDT-16 The font and size boxes show the current font as soon as a day opens")]
    public void ToolbarFilledOnLoad() => AppThread.Run(async () =>
    {
        var (window, _) = await OpenLongestAsync(1240, 820);
        try
        {
            var editor = AppThread.FindChild<JournalEditor>(window)!;
            Assert.Equal("Segoe UI", editor.FontBox.SelectedItem as string);
            Assert.Equal("11", editor.SizeBox.Text);
        }
        finally { window.Close(); }
    });
}
