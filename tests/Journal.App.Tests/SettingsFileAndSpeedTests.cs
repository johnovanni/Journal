using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Journal.App.Controls;
using Journal.App.Services;
using Journal.App.ViewModels;
using Journal.App.Views;
using Journal.Core.Documents;
using Journal.Core.Domain;
using Journal.Core.Inspiration;
using Journal.Core.Security;
using Journal.Core.Services;
using Journal.Core.Settings;
using Journal.Core.Tests.Support;
using Journal.Infrastructure;
using Journal.Infrastructure.Storage;
using Xunit.Abstractions;

namespace Journal.App.Tests;

/// <summary>ISS-025: what the settings file may hold, and how fast a day opens, against a real vault.</summary>
public class SettingsFileAndSpeedTests(ITestOutputHelper output)
{
    private sealed class OpenJournal : IOpenJournal
    {
        public AppSettings Settings { get; set; } = new();
        public string VaultFolder { get; set; } = "";
        public VaultSession? Session { get; set; }
        public VaultServices? Services { get; set; }
        public MainViewModel? Main { get; set; }
    }

    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "journal-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static async Task<(VaultSession Session, VaultServices Services)> OpenVaultAsync(string folder, FakeClock clock)
    {
        var machine = new FakeMachine();
        var session = new VaultSession(new InMemoryHeaderStore(),
            [new FakeKeyProtector(KeySlotTypes.WindowsHello) { Machine = machine }, new FakeKeyProtector(KeySlotTypes.RecoveryCode)], machine, clock);
        await session.CreateVaultAsync(useHello: true);
        return (session, await VaultServices.OpenAsync(session, folder, clock, () => new InspirationSettings()));
    }

    [Fact(DisplayName = "TC-NFR-09 The settings file holds no journal content")]
    public void SettingsHoldNoContent() => Sta.Run(async () =>
    {
        const string marker = "ZZQX";
        var folder = NewFolder();
        var clock = new FakeClock();
        var (session, services) = await OpenVaultAsync(folder, clock);
        using var _ = services;

        // Journal content of every kind, each carrying the marker.
        var entry = (await services.Journal.OpenDayAsync(clock.Today)).Entry!;
        var file = await services.Journal.AddBlobAsync(new MemoryStream([1, 2, 3]));
        entry.DefaultTab.Document.Blocks.AddRange([
            ParagraphBlock.OfText($"text {marker}"),
            new ImageBlock { Image = file, Caption = $"caption {marker}" },
            new AttachmentBlock { File = file, FileName = $"{marker}.txt", Description = $"description {marker}" },
        ]);
        entry.AddTag(Tag.Parse($"tag{marker}"));
        entry.AddTab(Tag.Parse($"tab{marker}")).Document.Blocks.Add(ParagraphBlock.OfText($"tab text {marker}"));
        entry.SetMood(Mood.Happy, clock.Today);
        await services.Journal.SaveAsync(entry);

        // Settings saved the way the app saves them, after importing lists and ticking everything on offer.
        var settingsPath = Path.Combine(NewFolder(), "settings.json");
        var store = new JsonSettingsStore(settingsPath);
        var journal = new OpenJournal { Session = session, Services = services, VaultFolder = folder };
        var locks = new LockCoordinator(clock, () => session, () => { }, Dispatcher.CurrentDispatcher);
        var actions = new SettingsActions(journal, store, locks, new FakeDialogs(), () => { });
        var verses = Path.Combine(folder, "v.csv");
        var quotes = Path.Combine(folder, "q.csv");
        await File.WriteAllTextAsync(verses, $"reference,text,translation\nJohn 3:16,verse {marker},{marker}\n");
        await File.WriteAllTextAsync(quotes, $"text,author,work,tradition,category\nquote {marker},author{marker},work{marker},trad{marker},cat{marker}\n");
        await actions.ImportVersesAsync(verses);
        await actions.ImportQuotesAsync(quotes);

        var vm = new SettingsViewModel(actions, new FakeDialogs(), new AppSettings { VaultFolder = folder }) { UseImportedQuotes = true, UseImportedVerses = true };
        foreach (var section in vm.VerseSections)
        {
            section.IsIncluded = true;
            foreach (var book in section.Books) book.IsChecked = true;
        }
        foreach (var section in vm.CategorySections)
            foreach (var tradition in section.Traditions) tradition.IsChecked = true;
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains("philosophy/", saved, StringComparison.OrdinalIgnoreCase);   // the file really has the ticked choices
        Assert.DoesNotContain(marker, saved, StringComparison.OrdinalIgnoreCase);

        // A new setting must be a deliberate choice: check this list against NFR-SEC-07 before adding to it.
        using var doc = JsonDocument.Parse(saved);
        Assert.Equal(["attachmentNoticeShown", "autoLockMinutes", "formatVersion", "inspiration", "vaultFolder", "weekStart"],
            doc.RootElement.EnumerateObject().Select(p => p.Name).Where(n => n != "autoLockTimeout").Order(StringComparer.Ordinal));
        Assert.Equal(["hiddenCategories", "mode", "quoteSource", "showQuote", "showVerse", "traditions", "verseBooks", "verseSource", "verseTraditions"],
            doc.RootElement.GetProperty("inspiration").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    });

    /// <summary>A noisy PNG of about 1 MB, which doesn't compress much, like a photo.</summary>
    private static byte[] MegabyteImage(int seed)
    {
        const int size = 520;
        var pixels = new byte[size * size * 4];
        new Random(seed).NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return ImageProbe.EncodePng(BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4));
    }

    [PerfFact(DisplayName = "TC-NFR-14 Switching days with 3,650 entries: ≤ 300 ms for text, ≤ 1 s with 10 images")]
    public void SwitchingDays() => AppThread.Run(async () =>
    {
        var folder = NewFolder();
        var clock = new FakeClock();
        var (session, first) = await OpenVaultAsync(folder, clock);

        const int rounds = 5;
        var start = new DateOnly(2016, 1, 1);
        var words = string.Concat(Enumerable.Repeat("words of the day and more words besides ", 30));
        for (int i = 0; i < 3650; i++)
            await first.Entries.SaveAsync(TestData.Entry(start.AddDays(i), $"Day {i} {words}"));

        // Days with 10 different 1 MB images, a fresh set per round, so the image cache can't help.
        var imageDays = new List<DateOnly>();
        for (int r = 0; r < rounds; r++)
        {
            var date = start.AddDays(4000 + r);
            imageDays.Add(date);
            var entry = TestData.Entry(date, "With pictures");
            for (int i = 0; i < 10; i++)
            {
                var bytes = MegabyteImage(r * 100 + i);
                Assert.InRange(bytes.Length, 900_000, 1_300_000);
                var blob = await first.Journal.AddBlobAsync(new MemoryStream(bytes));
                entry.DefaultTab.Document.Blocks.Add(new ImageBlock { Image = blob, Caption = $"Picture {i}", PixelWidth = 520, PixelHeight = 520 });
            }
            await first.Entries.SaveAsync(entry);
        }

        // Reopen so the catalog is rebuilt from all those entries, as after a real unlock.
        first.Dispose();
        File.Delete(new VaultPaths(folder).Catalog);
        using var services = await VaultServices.OpenAsync(session, folder, clock, () => new InspirationSettings());
        var settings = new AppSettings();
        var images = new ImageCache(b => services.Journal.ReadBlobAsync(b));
        var vm = new MainViewModel(new JournalContext
        {
            Journal = services.Journal,
            Dialogs = new FakeDialogs(),
            Scheduler = new ManualScheduler(),
            Clock = clock,
            Temp = new TempFileManager(NewFolder()),
            Shell = new FakeShell(),
            Clipboard = new FakeClipboard(),
            Images = images,
            Settings = () => settings,
            SaveSettings = s => settings = s,
        });
        var window = new MainWindow(vm)
        {
            Width = 1240, Height = 820, Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
        };
        try
        {
            window.Show();
            await vm.InitializeAsync();
            await AppThread.Settle();
            await vm.SelectDateAsync(start.AddDays(1000));   // warm up: first use of the editor and the JIT
            await AppThread.Settle();

            int saves = 0;
            services.Journal.EntryChanged += (_, _) => saves++;
            var breakdown = new List<string>();
            var text = new List<double>();
            var pictures = new List<double>();
            var shown = new List<int>();
            for (int r = 0; r < rounds; r++)
            {
                var sw = Stopwatch.StartNew();
                await vm.SelectDateAsync(start.AddDays(2000 + r));
                var selected = sw.Elapsed.TotalMilliseconds;
                await AppThread.Settle();
                text.Add(sw.Elapsed.TotalMilliseconds);
                breakdown.Add($"{selected:0}+{sw.Elapsed.TotalMilliseconds - selected:0}");

                sw.Restart();
                await vm.SelectDateAsync(imageDays[r]);
                await AppThread.Settle();
                var editor = AppThread.FindChild<JournalEditor>(window)!;
                var views = editor.TextBox.Document.Blocks.OfType<BlockUIContainer>().Select(b => b.Child).OfType<ImageBlockView>().ToList();
                Assert.Equal(10, views.Count);
                while (views.Any(v => v.LoadedSource is null) && sw.Elapsed < TimeSpan.FromSeconds(20))
                    await Task.Delay(5);
                pictures.Add(sw.Elapsed.TotalMilliseconds);
                shown.Add(views.Count(v => v.LoadedSource is not null));
            }

            double textMedian = text.Order().ElementAt(rounds / 2), picturesMedian = pictures.Order().ElementAt(rounds / 2);
            output.WriteLine($"text-only ms: {string.Join(", ", text.Select(t => t.ToString("0")))}; median {textMedian:0}; open+show: {string.Join(", ", breakdown)}; saves during the run: {saves}");
            output.WriteLine($"10 images ms: {string.Join(", ", pictures.Select(t => t.ToString("0")))}; median {picturesMedian:0}; loaded {string.Join(",", shown)}");
            Assert.All(shown, n => Assert.Equal(10, n));
            Assert.True(textMedian <= 300, $"text-only median {textMedian:0} ms");
            Assert.True(picturesMedian <= 1000, $"10-image median {picturesMedian:0} ms");
        }
        finally { window.Close(); }
    }, TimeSpan.FromMinutes(10));
}
