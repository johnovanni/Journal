using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Journal.App.Controls;
using Journal.App.Editor;
using Journal.App.ViewModels;
using Journal.App.Views;
using Journal.Core.Documents;
using Journal.Core.Tests.Support;

namespace Journal.App.Tests;

/// <summary>ISS-019: each editor hears only its own image and file blocks and supplies their image loader.</summary>
public class BlockRoutingTests
{
    private static readonly BlobRef Photo = new(Guid.NewGuid(), "sha");

    private static byte[] SupportPng(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        new Random(width).NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return Journal.App.Services.ImageProbe.EncodePng(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4));
    }

    /// <summary>A real editor showing one tab of today's entry, with one image and one file.</summary>
    private static async Task<(JournalEditor Editor, EntryTabViewModel Tab, Window Host)> OpenAsync(DateOnly date)
    {
        var h = new AppHarness();
        var main = await h.MainAsync();
        await main.SelectDateAsync(date);
        var tab = main.CurrentEntry!.Tabs[0];
        tab.Model.Document = new JournalDocument([
            new ImageBlock { Image = Photo, Caption = "Garden", PixelWidth = 4, PixelHeight = 4 },
            new AttachmentBlock { File = new BlobRef(Guid.NewGuid(), "s"), FileName = "a.pdf", SizeBytes = 1 },
            ParagraphBlock.OfText("words"),
        ]);
        var editor = new JournalEditor { DataContext = tab };
        var host = new Window { Content = editor, Left = -20000, Width = 700, Height = 500, ShowActivated = false, ShowInTaskbar = false };
        host.Show();
        await AppThread.Settle();
        return (editor, tab, host);
    }

    private static T Block<T>(JournalEditor e) where T : UIElement =>
        (T)e.TextBox.Document.Blocks.OfType<BlockUIContainer>().Select(b => b.Child).OfType<T>().Single();

    [Fact(DisplayName = "TC-IMG-05 With two editors open, an edit marks only the editor it happened in")]
    public void EditsStayInTheirEditor() => AppThread.Run(async () =>
    {
        var (a, tabA, hostA) = await OpenAsync(TestData.Today);
        var (b, tabB, hostB) = await OpenAsync(TestData.Today.AddDays(1));
        try
        {
            Block<ImageBlockView>(a).Caption = "Changed";
            Assert.True(tabA.Owner.IsDirty);
            Assert.False(tabB.Owner.IsDirty);

            tabA.Owner.CancelPendingSave();
            Block<AttachmentChip>(b).Description = "Changed too";
            Assert.False(tabA.Owner.IsDirty);
            Assert.True(tabB.Owner.IsDirty);
            tabB.Owner.CancelPendingSave();
        }
        finally { hostA.Close(); hostB.Close(); }
    });

    [Fact(DisplayName = "TC-IMG-05 Removing an image or file acts only in its own editor")]
    public void ActionsStayInTheirEditor() => AppThread.Run(async () =>
    {
        var (a, tabA, hostA) = await OpenAsync(TestData.Today);
        var (b, tabB, hostB) = await OpenAsync(TestData.Today.AddDays(1));
        try
        {
            Block<ImageBlockView>(a).Apply(ImageAction.Remove);
            await AppThread.Settle();
            Assert.Empty(a.Capture().Images());
            Assert.Single(b.Capture().Images());

            Block<AttachmentChip>(b).Request(AttachmentAction.Remove);
            await AppThread.Settle();
            Assert.Single(a.Capture().Attachments());
            Assert.Empty(b.Capture().Attachments());
            tabA.Owner.CancelPendingSave();
            tabB.Owner.CancelPendingSave();
        }
        finally { hostA.Close(); hostB.Close(); }
    });

    /// <summary>The real main window on today, with two earlier days that each hold a real image.</summary>
    private static async Task<(MainWindow Window, MainViewModel Vm, AppHarness H, DateOnly DayA, DateOnly DayB)> OpenMainWindowAsync()
    {
        var h = new AppHarness();
        var dayA = h.Clock.Today.AddDays(-1);
        var dayB = h.Clock.Today.AddDays(-2);
        foreach (var day in new[] { dayA, dayB })
        {
            var blob = await h.Journal.AddBlobAsync(new MemoryStream(SupportPng(16, 16)));
            var entry = TestData.Entry(day, "pictures");
            entry.DefaultTab.Document.Blocks.Add(new ImageBlock { Image = blob, Caption = "Garden", PixelWidth = 16, PixelHeight = 16 });
            await h.Journal.SaveAsync(entry);
        }
        var vm = new MainViewModel(h.Context);
        var window = new MainWindow(vm)
        {
            Width = 1240, Height = 820, Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.Show();
        await vm.InitializeAsync();
        await AppThread.Settle();
        return (window, vm, h, dayA, dayB);
    }

    private static async Task<ImageBlockView> ShownImageAsync(MainWindow window)
    {
        var view = Block<ImageBlockView>(AppThread.FindChild<JournalEditor>(window)!);
        for (int i = 0; i < 100 && view.LoadedSource is null; i++)
        {
            await Task.Delay(20);
            await AppThread.Settle();
        }
        return view;
    }

    [Fact(DisplayName = "TC-IMG-11 In the real window, images load on every day opened, not just the first")]
    public void ImagesLoadAcrossDays() => AppThread.Run(async () =>
    {
        var (window, vm, _, dayA, dayB) = await OpenMainWindowAsync();
        try
        {
            foreach (var day in new[] { dayA, dayB, dayA })
            {
                await vm.SelectDateAsync(day);
                await AppThread.Settle();
                Assert.NotNull((await ShownImageAsync(window)).LoadedSource);
            }
        }
        finally { window.Close(); }
    });

    [Fact(DisplayName = "TC-IMG-11 In the real window, an image re-created as undo does still loads")]
    public void RecreatedImageLoads() => AppThread.Run(async () =>
    {
        var (window, vm, _, dayA, _) = await OpenMainWindowAsync();
        try
        {
            await vm.SelectDateAsync(dayA);
            await AppThread.Settle();
            var original = await ShownImageAsync(window);
            var editor = AppThread.FindChild<JournalEditor>(window)!;
            var xaml = System.Windows.Markup.XamlWriter.Save(original.Parent);
            var copy = (BlockUIContainer)System.Windows.Markup.XamlReader.Parse(xaml);
            editor.TextBox.Document.Blocks.Add(copy);
            await AppThread.Settle();
            var view = (ImageBlockView)copy.Child;
            for (int i = 0; i < 100 && view.LoadedSource is null; i++)
            {
                await Task.Delay(20);
                await AppThread.Settle();
            }
            Assert.NotNull(view.LoadedSource);
            vm.CurrentEntry!.CancelPendingSave();
        }
        finally { window.Close(); }
    });

    [Fact(DisplayName = "TC-IMG-11 In the real window, edits and actions reach the editor after switching days")]
    public void ActionsAcrossDays() => AppThread.Run(async () =>
    {
        var (window, vm, _, dayA, dayB) = await OpenMainWindowAsync();
        try
        {
            await vm.SelectDateAsync(dayA);
            await vm.SelectDateAsync(dayB);
            await AppThread.Settle();
            var view = await ShownImageAsync(window);
            view.Caption = "Changed";
            Assert.True(vm.CurrentEntry!.IsDirty);
            view.Apply(ImageAction.Remove);
            await AppThread.Settle();
            Assert.Empty(AppThread.FindChild<JournalEditor>(window)!.Capture().Images());
            vm.CurrentEntry.CancelPendingSave();
        }
        finally { window.Close(); }
    });

    [Fact(DisplayName = "TC-IMG-11 With no open day, an image gets no answer, so nothing can load after a lock")]
    public void NoLoadWithoutADay() => AppThread.Run(async () =>
    {
        var (editor, tab, host) = await OpenAsync(TestData.Today);
        try
        {
            tab.Owner.CancelPendingSave();
            var view = Block<ImageBlockView>(editor);
            ImageLoadEventArgs Ask() => new(BlockEvents.ImageLoadEvent, view, Photo, 16);

            var open = Ask();
            view.RaiseEvent(open);
            Assert.NotNull(open.Result);

            editor.DataContext = null;     // locking clears the open day
            var kept = (BlockUIContainer)System.Windows.Markup.XamlReader.Parse(System.Windows.Markup.XamlWriter.Save(view.Parent));
            editor.TextBox.Document.Blocks.Add(kept);
            var closed = new ImageLoadEventArgs(BlockEvents.ImageLoadEvent, kept.Child, Photo, 16);
            kept.Child.RaiseEvent(closed);
            Assert.Null(closed.Result);
        }
        finally { host.Close(); }
    });
}
