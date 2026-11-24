using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Journal.App.Controls;
using Journal.App.ViewModels;
using Journal.App.Views;
using Journal.Core.Documents;
using Journal.Core.Domain;
using Journal.Core.Tests.Support;

namespace Journal.App.Tests;

/// <summary>ISS-026: looking at a day must not save it; a real change still does.</summary>
public class DaySwitchTests
{
    private static readonly DateOnly Rich = TestData.Today.AddDays(-3);
    private static readonly DateOnly Plain = TestData.Today.AddDays(-2);

    /// <summary>A day with every kind of content the editor shows: formatting, headings, lists, checklists, images, files and a second tab.</summary>
    private static async Task SaveRichDayAsync(AppHarness h)
    {
        var entry = TestData.Entry(Rich);
        var doc = entry.DefaultTab.Document;
        doc.Blocks.Clear();
        var image = await h.Journal.AddBlobAsync(new MemoryStream(Sta.Png(40, 20)));
        var file = await h.Journal.AddBlobAsync(new MemoryStream([1, 2, 3, 4]));
        doc.Blocks.AddRange([
            new ParagraphBlock(new TextRun("A heading")) { Style = ParagraphStyle.Heading1 },
            new ParagraphBlock(new TextRun("Plain, "), new TextRun("bold", RunFormat.Bold), new TextRun(" and "),
                new TextRun("red italic", RunFormat.Italic) { Foreground = ArgbColor.Parse("#FFC00000"), FontSize = 14 }),
            new ParagraphBlock(new TextRun("Centred")) { Alignment = TextAlign.Center },
            new ParagraphBlock(new TextRun("first point")) { List = ListKind.Bulleted },
            new ParagraphBlock(new TextRun("nested point")) { List = ListKind.Bulleted, ListLevel = 1 },
            new ParagraphBlock(new TextRun("step one")) { List = ListKind.Numbered },
            new ParagraphBlock(new TextRun("step two")) { List = ListKind.Numbered },
            new ParagraphBlock(new TextRun("done item")) { List = ListKind.Checklist, IsChecked = true },
            new ParagraphBlock(new TextRun("open item")) { List = ListKind.Checklist },
            new ImageBlock { Image = image, Caption = "A small picture", PixelWidth = 40, PixelHeight = 20, DisplayWidth = 200 },
            new AttachmentBlock { File = file, FileName = "notes.txt", ContentType = "text/plain", SizeBytes = 4, Description = "some notes" },
            ParagraphBlock.OfText("Line one\nline two after a line break"),
        ]);
        entry.AddTab(Tag.Parse("work")).Document.Blocks.Add(ParagraphBlock.OfText("Work notes"));
        entry.SetMood(Mood.Happy, TestData.Today);
        await h.Journal.SaveAsync(entry);
        await h.Journal.SaveAsync(TestData.Entry(Plain, "Just a few words."));
    }

    private static async Task<(MainWindow Window, MainViewModel Vm)> OpenAsync(AppHarness h)
    {
        var vm = new MainViewModel(h.Context);
        var window = new MainWindow(vm)
        {
            Width = 1240, Height = 820, Left = -20000, Top = 0,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.Show();
        await vm.InitializeAsync();
        await AppThread.Settle();
        return (window, vm);
    }

    private static async Task GoAsync(MainViewModel vm, DateOnly date)
    {
        await vm.SelectDateAsync(date);
        await AppThread.Settle();
    }

    [Fact(DisplayName = "TC-ENT-10 Opening and leaving a day without changing it saves nothing")]
    public void ViewingDoesNotSave() => AppThread.Run(async () =>
    {
        var h = new AppHarness();
        await SaveRichDayAsync(h);
        var modified = (await h.Repo.GetAsync(Rich))!.ModifiedUtc;
        var (window, vm) = await OpenAsync(h);
        try
        {
            int saves = 0;
            h.Journal.EntryChanged += (_, _) => saves++;
            h.Clock.Advance(TimeSpan.FromMinutes(5));

            for (int i = 0; i < 3; i++)
            {
                await GoAsync(vm, Rich);
                // Look at the second tab too, then go back to the first.
                vm.CurrentEntry!.SelectedTab = vm.CurrentEntry.Tabs[1];
                await AppThread.Settle();
                vm.CurrentEntry.SelectedTab = vm.CurrentEntry.Tabs[0];
                await AppThread.Settle();
                await GoAsync(vm, Plain);
            }
            await vm.FlushAsync();

            Assert.Equal(0, saves);
            Assert.Equal(modified, (await h.Repo.GetAsync(Rich))!.ModifiedUtc);
        }
        finally { window.Close(); }
    });

    [Fact(DisplayName = "TC-ENT-11 A real change still saves when leaving the day: typing, or ticking a checkbox")]
    public void RealChangeSaves() => AppThread.Run(async () =>
    {
        var h = new AppHarness();
        await SaveRichDayAsync(h);
        var (window, vm) = await OpenAsync(h);
        try
        {
            int saves = 0;
            h.Journal.EntryChanged += (_, _) => saves++;

            // Typing.
            await GoAsync(vm, Rich);
            var editor = AppThread.FindChild<JournalEditor>(window)!;
            editor.TextBox.Document.ContentEnd.InsertTextInRun(" more");
            await GoAsync(vm, Plain);
            Assert.Equal(1, saves);
            Assert.Contains("more", (await h.Repo.GetAsync(Rich))!.DefaultTab.Document.ExtractPlainText());

            // Ticking a checkbox changes no text, but is still a change.
            await GoAsync(vm, Rich);
            editor = AppThread.FindChild<JournalEditor>(window)!;
            var open = editor.TextBox.Document.Blocks.OfType<Paragraph>()
                .Select(Editor.FlowDocumentMapper.GetCheckBox).First(c => c is { IsChecked: false })!;
            open.IsChecked = true;
            await GoAsync(vm, Plain);
            Assert.Equal(2, saves);
            Assert.Equal((0, 2), (await h.Repo.GetAsync(Rich))!.DefaultTab.Document.ChecklistCounts());
        }
        finally { window.Close(); }
    });
}
