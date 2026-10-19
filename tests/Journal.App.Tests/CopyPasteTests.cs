using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Journal.App.Editor;
using Journal.Core.Documents;
using Journal.Core.Persistence;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace Journal.App.Tests;

/// <summary>
/// ISS-010: copy, cut, paste and drag inside the journal keep every kind of content. The copy runs through
/// the editor's real Copy command and copying handler; the copied data is captured instead of being written
/// to the Windows clipboard, then handed to the editor's pasting handler.
/// </summary>
public class CopyPasteTests
{
    private static readonly FlowDocumentMapper Mapper = new();
    private static readonly ArgbColor Red = ArgbColor.Parse("#FFC00000");
    private static readonly ArgbColor Yellow = ArgbColor.Parse("#FFFFFF00");
    private static readonly BlobRef Photo = new(Guid.NewGuid(), "sha-photo");
    private static readonly BlobRef Sheet = new(Guid.NewGuid(), "sha-sheet");

    public static TheoryData<string> Cases => [.. AllCases.Keys];

    private static readonly Dictionary<string, DocBlock[]> AllCases = new()
    {
        ["Plain text"] = [ParagraphBlock.OfText("Plain words")],
        ["Bold, italic, underline"] = [new ParagraphBlock(new TextRun("b", RunFormat.Bold), new TextRun("i", RunFormat.Italic), new TextRun("u", RunFormat.Underline))],
        ["Strikethrough"] = [new ParagraphBlock(new TextRun("struck", RunFormat.Strikethrough))],
        ["Font, size, colour, highlight"] = [new ParagraphBlock(new TextRun("styled") { FontFamily = "Georgia", FontSize = 20, Foreground = Red, Highlight = Yellow })],
        ["Line break"] = [ParagraphBlock.OfText("line one\nline two")],
        ["Heading 1"] = [new ParagraphBlock(new TextRun("Title")) { Style = ParagraphStyle.Heading1 }],
        ["Heading 2"] = [new ParagraphBlock(new TextRun("Subtitle")) { Style = ParagraphStyle.Heading2 }],
        ["Centred paragraph"] = [new ParagraphBlock(new TextRun("middle")) { Alignment = TextAlign.Center }],
        ["Bulleted list"] = [new ParagraphBlock(new TextRun("a")) { List = ListKind.Bulleted }, new ParagraphBlock(new TextRun("b")) { List = ListKind.Bulleted }],
        ["Nested list"] = [new ParagraphBlock(new TextRun("outer")) { List = ListKind.Numbered }, new ParagraphBlock(new TextRun("inner")) { List = ListKind.Bulleted, ListLevel = 1 }],
        ["Checklist"] =
        [
            new ParagraphBlock(new TextRun("Buy seeds")) { List = ListKind.Checklist },
            new ParagraphBlock(new TextRun("Water beans")) { List = ListKind.Checklist, IsChecked = true },
            new ParagraphBlock(new TextRun("Sub task")) { List = ListKind.Checklist, ListLevel = 1 },
        ],
        ["Image with caption and size"] = [new ImageBlock { Image = Photo, Caption = "Garden", DisplayWidth = 240, PixelWidth = 10, PixelHeight = 10 }],
        ["Attached file"] = [new AttachmentBlock { File = Sheet, FileName = "list.xlsx", SizeBytes = 2048, Description = "yields" }],
    };

    /// <summary>Document content without block ids (pasted blocks get new ids).</summary>
    private static string Shape(JournalDocument d) => JournalJson.SerializeDocument(new JournalDocument(d.Blocks.Select(b => b switch
    {
        ParagraphBlock p => (DocBlock)new ParagraphBlock { Id = Guid.Empty, Style = p.Style, List = p.List, ListLevel = p.ListLevel, IsChecked = p.IsChecked, Alignment = p.Alignment, Runs = p.Runs },
        ImageBlock i => new ImageBlock { Id = Guid.Empty, Image = i.Image, Caption = i.Caption, DisplayWidth = i.DisplayWidth, PixelWidth = i.PixelWidth, PixelHeight = i.PixelHeight },
        AttachmentBlock a => new AttachmentBlock { Id = Guid.Empty, File = a.File, FileName = a.FileName, SizeBytes = a.SizeBytes, Description = a.Description, ContentType = a.ContentType },
        _ => b,
    })));

    /// <summary>An editor box set up as the journal's editor sets it up, shown off screen.</summary>
    private static (RichTextBox Box, Window Host) Editor(params DocBlock[] blocks)
    {
        var box = new RichTextBox { Document = Mapper.ToFlowDocument(new JournalDocument(blocks)), IsDocumentEnabled = true };
        JournalClipboard.Attach(box, () => true, () => { });
        var host = new Window { Content = box, Left = -20000, Width = 600, Height = 400, ShowActivated = false, ShowInTaskbar = false };
        host.Show();
        return (box, host);
    }

    /// <summary>Runs the editor's Copy command and returns what it would have put on the clipboard.</summary>
    private static IDataObject CopyFrom(RichTextBox box)
    {
        IDataObject? copied = null;
        DataObjectCopyingEventHandler capture = (_, e) => { copied = e.DataObject; e.CancelCommand(); };
        DataObject.AddCopyingHandler(box, capture);
        ApplicationCommands.Copy.Execute(null, box);
        DataObject.RemoveCopyingHandler(box, capture);
        return copied ?? throw new InvalidOperationException("Copy produced no data.");
    }

    /// <summary>Sends <paramref name="data"/> to the box's pasting handlers, as Ctrl+V or a drop does.</summary>
    private static DataObjectPastingEventArgs PasteInto(RichTextBox box, IDataObject data, bool isDragDrop = false)
    {
        var args = new DataObjectPastingEventArgs(data, isDragDrop, DataFormats.Xaml) { RoutedEvent = DataObject.PastingEvent };
        box.RaiseEvent(args);
        return args;
    }

    [Theory(DisplayName = "TC-EDT-17 Copy and paste within the journal keeps each kind of content")]
    [MemberData(nameof(Cases))]
    public void CopyPasteKeepsContent(string name) => Sta.Run(() =>
    {
        var doc = new JournalDocument([ParagraphBlock.OfText("Before"), .. AllCases[name], ParagraphBlock.OfText("After")]);
        var (source, h1) = Editor([.. doc.Blocks]);
        var (target, h2) = Editor();
        source.SelectAll();
        var data = CopyFrom(source);
        Assert.True(data.GetDataPresent(JournalClipboard.Format), $"{name}: the copy has no journal content");

        var args = PasteInto(target, data);
        Assert.True(args.CommandCancelled);   // the journal pasted it; WPF's own paste doesn't run as well
        Assert.Equal(Shape(doc), Shape(Mapper.FromFlowDocument(target.Document)));
        h1.Close(); h2.Close();
    });

    [Fact(DisplayName = "TC-EDT-17 Part of one plain paragraph is left to the normal copy; across paragraphs the selected text is kept")]
    public void PartialSelections() => Sta.Run(() =>
    {
        var (box, host) = Editor(
            new ParagraphBlock(new TextRun("Hello "), new TextRun("wonderful", RunFormat.Bold), new TextRun(" world")),
            new ParagraphBlock(new TextRun("Buy seeds today")) { List = ListKind.Checklist, IsChecked = true });
        var first = (WpfParagraph)box.Document.Blocks.FirstBlock;
        var second = (WpfParagraph)first.NextBlock;

        // Inside one plain paragraph: WPF's copy already keeps formatting, so no journal content is added.
        box.Selection.Select(first.ContentStart.GetPositionAtOffset(3), first.ContentStart.GetPositionAtOffset(10));
        Assert.Null(JournalClipboard.Fragment(box));

        // From "wonderful" in the first paragraph to "Buy" in the checklist item.
        box.Selection.Select(FindText(first, "wonderful"), FindText(second, " seeds"));
        var fragment = JournalClipboard.Fragment(box)!;
        Assert.Equal(2, fragment.Blocks.Count);
        var p1 = (ParagraphBlock)fragment.Blocks[0];
        var p2 = (ParagraphBlock)fragment.Blocks[1];
        Assert.Equal("wonderful world", p1.PlainText);
        Assert.Equal(RunFormat.Bold, p1.Runs[0].Format);
        Assert.Equal("Buy", p2.PlainText);
        Assert.Equal(ListKind.Checklist, p2.List);
        Assert.True(p2.IsChecked);
        host.Close();
    });

    private static TextPointer FindText(WpfParagraph p, string text)
    {
        for (var pos = p.ContentStart; pos is not null && pos.CompareTo(p.ContentEnd) < 0; pos = pos.GetNextInsertionPosition(LogicalDirection.Forward))
            if (new TextRange(pos, p.ContentEnd).Text.StartsWith(text, StringComparison.Ordinal)) return pos;
        throw new InvalidOperationException($"'{text}' not found");
    }

    [Fact(DisplayName = "TC-EDT-17 A selected heading or checklist line on its own keeps its style")]
    public void WholeStyledParagraph() => Sta.Run(() =>
    {
        var (box, host) = Editor(new ParagraphBlock(new TextRun("Plans")) { Style = ParagraphStyle.Heading1 }, ParagraphBlock.OfText("x"));
        var heading = (WpfParagraph)box.Document.Blocks.FirstBlock;
        box.Selection.Select(heading.ContentStart, heading.ContentEnd);
        var fragment = JournalClipboard.Fragment(box)!;
        Assert.Equal(ParagraphStyle.Heading1, Assert.IsType<ParagraphBlock>(Assert.Single(fragment.Blocks)).Style);
        host.Close();
    });

    [Fact(DisplayName = "TC-EDT-17 Cut and paste of an image keeps the same vault image")]
    public void CutPasteImage() => Sta.Run(() =>
    {
        var image = new ImageBlock { Image = Photo, Caption = "Garden", PixelWidth = 10, PixelHeight = 10 };
        var (source, h1) = Editor(ParagraphBlock.OfText("Before"), image, ParagraphBlock.OfText("After"));
        var (target, h2) = Editor(ParagraphBlock.OfText("Other day"));
        source.SelectAll();
        var data = CopyFrom(source);
        source.Selection.Text = "";   // the cut removes the original
        Assert.DoesNotContain(Photo, Mapper.FromFlowDocument(source.Document).ReferencedBlobs());

        target.CaretPosition = target.Document.ContentEnd;
        PasteInto(target, data);
        var pasted = Mapper.FromFlowDocument(target.Document);
        // The pasted entry references the same encrypted image, so the vault clean-up keeps it (NFR-REL-03).
        Assert.Contains(Photo, pasted.ReferencedBlobs());
        Assert.Equal(["Other day", "Before", "After"], pasted.Paragraphs().Select(p => p.PlainText));
        h1.Close(); h2.Close();
    });

    [Fact(DisplayName = "TC-EDT-17 Pasting is one undo step and replaces the selection")]
    public void UndoAndReplace() => Sta.Run(() =>
    {
        var (source, h1) = Editor(new ParagraphBlock(new TextRun("Task")) { List = ListKind.Checklist }, new AttachmentBlock { File = Sheet, FileName = "a.pdf" });
        var (target, h2) = Editor(ParagraphBlock.OfText("Keep"), ParagraphBlock.OfText("Replace me"));
        source.SelectAll();
        var data = CopyFrom(source);
        var second = (WpfParagraph)target.Document.Blocks.LastBlock;
        target.Selection.Select(second.ContentStart, second.ContentEnd);
        PasteInto(target, data);
        var after = Mapper.FromFlowDocument(target.Document);
        Assert.DoesNotContain(after.Paragraphs(), p => p.PlainText == "Replace me");
        Assert.Single(after.Attachments());
        Assert.Contains(after.Paragraphs(), p => p.PlainText == "Task" && p.List == ListKind.Checklist);

        target.Undo();
        Assert.Equal(["Keep", "Replace me"], Mapper.FromFlowDocument(target.Document).Paragraphs().Select(p => p.PlainText));
        h1.Close(); h2.Close();
    });

    [Fact(DisplayName = "TC-EDT-17 Dragging journal content keeps it and lets the drop finish")]
    public void DragAndDrop() => Sta.Run(() =>
    {
        var (source, h1) = Editor(ParagraphBlock.OfText("a"), new ImageBlock { Image = Photo, PixelWidth = 1, PixelHeight = 1 }, ParagraphBlock.OfText("b"));
        var (target, h2) = Editor();
        source.SelectAll();
        var data = CopyFrom(source);
        var args = PasteInto(target, data, isDragDrop: true);
        Assert.False(args.CommandCancelled);   // a move still removes the dragged original
        Assert.Equal("", args.DataObject.GetData(DataFormats.UnicodeText));
        Assert.Single(Mapper.FromFlowDocument(target.Document).Images());
        h1.Close(); h2.Close();
    });

    [Fact(DisplayName = "TC-EDT-17 Other apps get checklist lines with ☐ and ☑")]
    public void PlainTextForOtherApps() => Sta.Run(() =>
    {
        var (box, host) = Editor(
            ParagraphBlock.OfText("Garden"),
            new ParagraphBlock(new TextRun("Buy seeds")) { List = ListKind.Checklist },
            new ParagraphBlock(new TextRun("Water beans")) { List = ListKind.Checklist, IsChecked = true, ListLevel = 1 });
        box.SelectAll();
        var text = (string)CopyFrom(box).GetData(DataFormats.UnicodeText);
        Assert.Equal(string.Join(Environment.NewLine, "Garden", "☐ Buy seeds", "  ☑ Water beans"), text);
        host.Close();
    });
}
