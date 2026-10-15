using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using Journal.App.Controls;
using Journal.App.Editor;
using Journal.Core.Documents;
using Journal.Core.Persistence;
using DocList = System.Windows.Documents.List;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace Journal.App.Tests;

public class MapperTests
{
    private static readonly FlowDocumentMapper Mapper = new();

    private static JournalDocument RoundTrip(JournalDocument doc) => Mapper.FromFlowDocument(Mapper.ToFlowDocument(doc));

    private static void AssertSame(JournalDocument expected, JournalDocument actual)
    {
        // Block ids are preserved for images/files; paragraphs get new ids, so compare without them.
        static string Shape(JournalDocument d) => JournalJson.SerializeDocument(new JournalDocument(d.Blocks.Select(b =>
        {
            if (b is ParagraphBlock p) return (DocBlock)new ParagraphBlock { Id = Guid.Empty, Style = p.Style, List = p.List, ListLevel = p.ListLevel, IsChecked = p.IsChecked, Alignment = p.Alignment, Runs = p.Runs };
            return b;
        })));
        Assert.Equal(Shape(expected), Shape(actual));
    }

    [Fact(DisplayName = "TC-EDT-02 Bold, italic, underline, strikethrough survive the round trip")]
    public void RunFormats() => Sta.Run(() =>
    {
        var doc = new JournalDocument([new ParagraphBlock(
            new TextRun("b", RunFormat.Bold), new TextRun("i", RunFormat.Italic), new TextRun("u", RunFormat.Underline),
            new TextRun("s", RunFormat.Strikethrough), new TextRun("all", RunFormat.Bold | RunFormat.Italic | RunFormat.Strikethrough))]);
        AssertSame(doc, RoundTrip(doc));
        var fd = Mapper.ToFlowDocument(doc);
        var runs = ((WpfParagraph)fd.Blocks.FirstBlock).Inlines.OfType<Run>().ToList();
        Assert.Contains(runs[2].TextDecorations, d => d.Location == TextDecorationLocation.Underline);
        Assert.Contains(runs[3].TextDecorations, d => d.Location == TextDecorationLocation.Strikethrough);
    });

    [Fact(DisplayName = "TC-EDT-04 Font family and sizes are kept; defaults stay null")]
    public void Fonts() => Sta.Run(() =>
    {
        var doc = new JournalDocument([new ParagraphBlock(
            new TextRun("g") { FontFamily = "Georgia" }, new TextRun("8") { FontSize = 8 }, new TextRun("12") { FontSize = 12 },
            new TextRun("72") { FontSize = 72 }, new TextRun("plain"))]);
        var back = RoundTrip(doc);
        AssertSame(doc, back);
        Assert.Null(((ParagraphBlock)back.Blocks[0]).Runs[^1].FontFamily);
        Assert.Null(((ParagraphBlock)back.Blocks[0]).Runs[^1].FontSize);
    });

    [Fact(DisplayName = "TC-EDT-06 Text and highlight colors are kept")]
    public void Colors() => Sta.Run(() =>
    {
        var doc = new JournalDocument([new ParagraphBlock(new TextRun("red") { Foreground = ArgbColor.Parse("#FFC00000"), Highlight = ArgbColor.Parse("#FFFFFF00") })]);
        AssertSame(doc, RoundTrip(doc));
    });

    [Fact(DisplayName = "TC-EDT-07 Heading styles are kept and not copied into runs")]
    public void Headings() => Sta.Run(() =>
    {
        var doc = new JournalDocument([
            new ParagraphBlock(new TextRun("Big")) { Style = ParagraphStyle.Heading1 },
            new ParagraphBlock(new TextRun("Smaller")) { Style = ParagraphStyle.Heading2 },
            ParagraphBlock.OfText("Body"),
        ]);
        var back = RoundTrip(doc);
        AssertSame(doc, back);
        Assert.Equal(RunFormat.None, ((ParagraphBlock)back.Blocks[0]).Runs[0].Format);
    });

    [Fact(DisplayName = "TC-EDT-08 Nested numbered and bulleted lists keep structure; numbering follows items")]
    public void NestedLists() => Sta.Run(() =>
    {
        var doc = new JournalDocument([
            new ParagraphBlock(new TextRun("one")) { List = ListKind.Numbered },
            new ParagraphBlock(new TextRun("two")) { List = ListKind.Numbered },
            new ParagraphBlock(new TextRun("a")) { List = ListKind.Bulleted, ListLevel = 1 },
            new ParagraphBlock(new TextRun("b")) { List = ListKind.Bulleted, ListLevel = 1 },
            new ParagraphBlock(new TextRun("deep")) { List = ListKind.Bulleted, ListLevel = 2 },
            new ParagraphBlock(new TextRun("three")) { List = ListKind.Numbered },
        ]);
        AssertSame(doc, RoundTrip(doc));
        var fd = Mapper.ToFlowDocument(doc);
        var top = Assert.IsType<DocList>(fd.Blocks.FirstBlock);
        Assert.Equal(TextMarkerStyle.Decimal, top.MarkerStyle);
        Assert.Equal(3, top.ListItems.Count);
        var nested = top.ListItems.ElementAt(1).Blocks.OfType<DocList>().Single();
        Assert.Equal(TextMarkerStyle.Disc, nested.MarkerStyle);
        top.ListItems.Remove(top.ListItems.FirstListItem);
        var after = Mapper.FromFlowDocument(fd);
        Assert.Equal("two", ((ParagraphBlock)after.Blocks[0]).PlainText);
        Assert.Equal(2, top.ListItems.Count);
    });

    [Fact(DisplayName = "TC-EDT-10 Checklist items keep ticked state; the checkbox is the first inline")]
    public void Checklists() => Sta.Run(() =>
    {
        var doc = new JournalDocument([
            new ParagraphBlock(new TextRun("Buy seeds")) { List = ListKind.Checklist },
            new ParagraphBlock(new TextRun("Water beans")) { List = ListKind.Checklist, IsChecked = true, ListLevel = 1 },
        ]);
        AssertSame(doc, RoundTrip(doc));
        var box = new RichTextBox { Document = Mapper.ToFlowDocument(doc) };
        var first = (WpfParagraph)box.Document.Blocks.FirstBlock;
        Assert.NotNull(FlowDocumentMapper.GetCheckBox(first));
        box.CaretPosition = first.ContentEnd;
        Assert.True(EditorOperations.ToggleChecked(box));
        Assert.True(((ParagraphBlock)Mapper.FromFlowDocument(box.Document).Blocks[0]).IsChecked);
    });

    [Fact(DisplayName = "TC-EDT-11 Ticked items show strikethrough without changing stored formatting")]
    public void TickedStrikethroughIsDisplayOnly() => Sta.Run(() =>
    {
        var doc = new JournalDocument([new ParagraphBlock(new TextRun("done")) { List = ListKind.Checklist, IsChecked = true }]);
        var fd = Mapper.ToFlowDocument(doc);
        var p = (WpfParagraph)fd.Blocks.FirstBlock;
        Assert.Contains(p.TextDecorations, d => d.Location == TextDecorationLocation.Strikethrough);
        Assert.Equal(RunFormat.None, ((ParagraphBlock)Mapper.FromFlowDocument(fd).Blocks[0]).Runs[0].Format);
        EditorOperations.ApplyCheckedDecoration(p, false);
        Assert.Empty(p.TextDecorations ?? []);
    });

    [Fact(DisplayName = "TC-EDT-13 500 random documents survive the FlowDocument round trip")]
    public void RandomDocuments() => Sta.Run(() =>
    {
        var rnd = new Random(42);
        string[] fonts = ["Georgia", "Consolas", "Arial"];
        for (int n = 0; n < 500; n++)
        {
            var blocks = new List<DocBlock>();
            int prevLevel = -1; // level of the previous bulleted/numbered item, or -1
            int count = rnd.Next(1, 8);
            for (int i = 0; i < count; i++)
            {
                switch (rnd.Next(5))
                {
                    case 0:
                        blocks.Add(new ImageBlock { Image = new BlobRef(Guid.NewGuid(), "x"), Caption = rnd.Next(2) == 0 ? "" : $"cap {i}", PixelWidth = 10, PixelHeight = 10 });
                        prevLevel = -1;
                        break;
                    case 1:
                        blocks.Add(new AttachmentBlock { File = new BlobRef(Guid.NewGuid(), "y"), FileName = $"f{i}.txt", SizeBytes = rnd.Next(1, 100000), Description = $"d{i}" });
                        prevLevel = -1;
                        break;
                    default:
                        var kind = (ListKind)rnd.Next(4);
                        int level = kind switch
                        {
                            ListKind.None => 0,
                            ListKind.Checklist => rnd.Next(4),
                            // A list item can be at most one level deeper than the previous list item.
                            _ => prevLevel < 0 ? 0 : rnd.Next(Math.Min(prevLevel + 1, 3) + 1),
                        };
                        var p = new ParagraphBlock
                        {
                            List = kind,
                            ListLevel = level,
                            IsChecked = kind == ListKind.Checklist && rnd.Next(2) == 0,
                            Style = kind == ListKind.None ? (ParagraphStyle)rnd.Next(3) : ParagraphStyle.Normal,
                        };
                        int runs = rnd.Next(1, 4);
                        for (int r = 0; r < runs; r++)
                        {
                            p.Runs.Add(new TextRun($"w{n}-{i}-{r} ")
                            {
                                Format = (RunFormat)rnd.Next(16),
                                FontFamily = rnd.Next(4) == 0 ? fonts[rnd.Next(fonts.Length)] : null,
                                FontSize = rnd.Next(4) == 0 ? new[] { 8.0, 12, 14, 72 }[rnd.Next(4)] : null,
                                Foreground = rnd.Next(5) == 0 ? new ArgbColor(255, (byte)rnd.Next(256), 0, 0) : null,
                                Highlight = rnd.Next(6) == 0 ? new ArgbColor(255, 255, 255, 0) : null,
                            });
                        }
                        p.NormalizeRuns();
                        blocks.Add(p);
                        prevLevel = kind is ListKind.Bulleted or ListKind.Numbered ? level : -1;
                        break;
                }
            }
            var doc = new JournalDocument(blocks);
            AssertSame(doc, RoundTrip(doc));
        }
    });

    [Fact(DisplayName = "TC-IMG-03 Inserting an image mid-paragraph splits it around the image")]
    public void InsertSplitsParagraph() => Sta.Run(() =>
    {
        var box = new RichTextBox { Document = Mapper.ToFlowDocument(new JournalDocument([ParagraphBlock.OfText("Hello world")])) };
        var p = (WpfParagraph)box.Document.Blocks.FirstBlock;
        box.CaretPosition = p.ContentStart.GetPositionAtOffset(1 + "Hello ".Length)!;
        var img = new ImageBlock { Image = new BlobRef(Guid.NewGuid(), "x") };
        EditorOperations.InsertBlockAtCaret(box, FlowDocumentMapper.ImageContainer(img));
        var blocks = Mapper.FromFlowDocument(box.Document).Blocks;
        Assert.Equal(3, blocks.Count);
        Assert.Equal("Hello ", ((ParagraphBlock)blocks[0]).PlainText);
        Assert.Equal(img.Image.Id, ((ImageBlock)blocks[1]).Image.Id);
        Assert.Equal("world", ((ParagraphBlock)blocks[2]).PlainText);
    });

    [Fact(DisplayName = "TC-IMG-04 Images between paragraphs keep their order")]
    public void ImageOrder() => Sta.Run(() =>
    {
        ImageBlock Img(string c) => new() { Image = new BlobRef(Guid.NewGuid(), "x"), Caption = c };
        var doc = new JournalDocument([ParagraphBlock.OfText("p1"), Img("a"), ParagraphBlock.OfText("p2"), Img("b"), Img("c"), ParagraphBlock.OfText("p3")]);
        AssertSame(doc, RoundTrip(doc));
        Assert.Equal(3, Mapper.ToFlowDocument(doc).Blocks.OfType<BlockUIContainer>().Count());
    });

    [Fact(DisplayName = "TC-IMG-05 Editing a caption updates the model and raises an edit event")]
    public void CaptionEdit() => Sta.Run(() =>
    {
        var view = ImageBlockView.From(new ImageBlock { Image = new BlobRef(Guid.NewGuid(), "x") });
        object? sender = null;
        void Handler(object? s, EventArgs e) => sender = s;
        BlockEvents.ContentEdited += Handler;
        try { view.Caption = "Garden at sunrise"; }
        finally { BlockEvents.ContentEdited -= Handler; }
        Assert.Same(view, sender);
        Assert.Equal("Garden at sunrise", view.ToBlock().Caption);
    });

    [Fact(DisplayName = "TC-IMG-06 Small/Medium/Full display width round trips")]
    public void DisplayWidth() => Sta.Run(() =>
    {
        var view = ImageBlockView.From(new ImageBlock { Image = new BlobRef(Guid.NewGuid(), "x"), PixelWidth = 4000, PixelHeight = 3000 });
        Assert.Null(view.ToBlock().DisplayWidth);
        view.Apply(ImageAction.Small);
        Assert.Equal(ImageBlockView.SmallWidth, view.ToBlock().DisplayWidth);
        var doc = new JournalDocument([view.ToBlock()]);
        AssertSame(doc, RoundTrip(doc));
        view.Apply(ImageAction.Full);
        Assert.Null(view.ToBlock().DisplayWidth);
    });

    [Fact(DisplayName = "TC-ATT-02 Attachment chips keep name, size and description")]
    public void Attachments() => Sta.Run(() =>
    {
        var att = new AttachmentBlock { File = new BlobRef(Guid.NewGuid(), "s"), FileName = "harvest-weights.xlsx", SizeBytes = 24 * 1024, Description = "tomato yields" };
        var doc = new JournalDocument([att]);
        AssertSame(doc, RoundTrip(doc));
        var chip = AttachmentChip.From(att);
        Assert.Equal("24 KB", chip.SizeText);
    });

    [Fact(DisplayName = "Embedded blocks survive WPF's XAML copy (used by undo and copy/paste)")]
    public void XamlRoundTripOfEmbeddedBlocks() => Sta.Run(() =>
    {
        var img = new ImageBlock { Image = new BlobRef(Guid.NewGuid(), "sha"), Caption = "cap", DisplayWidth = 240, PixelWidth = 10, PixelHeight = 5 };
        var container = FlowDocumentMapper.ImageContainer(img);
        var xaml = XamlWriter.Save(container);
        var copy = (BlockUIContainer)XamlReader.Parse(xaml);
        var view = Assert.IsType<ImageBlockView>(copy.Child);
        Assert.Equal(JournalJson.SerializeDocument(new JournalDocument([img])), JournalJson.SerializeDocument(new JournalDocument([view.ToBlock()])));

        var att = new AttachmentBlock { File = new BlobRef(Guid.NewGuid(), "s"), FileName = "a.pdf", SizeBytes = 99, Description = "d" };
        var chip = Assert.IsType<AttachmentChip>(((BlockUIContainer)XamlReader.Parse(XamlWriter.Save(FlowDocumentMapper.AttachmentContainer(att)))).Child);
        Assert.Equal("a.pdf", chip.ToBlock().FileName);
        Assert.Equal(99, chip.ToBlock().SizeBytes);
    });
}

public class EditorOperationTests
{
    private static readonly FlowDocumentMapper Mapper = new();

    private static RichTextBox Box(params DocBlock[] blocks) => new() { Document = Mapper.ToFlowDocument(new JournalDocument(blocks)) };

    [Fact(DisplayName = "TC-EDT-03 Strikethrough toggles on the selection")]
    public void StrikethroughToggle() => Sta.Run(() =>
    {
        var box = Box(ParagraphBlock.OfText("cross me out"));
        box.SelectAll();
        EditorOperations.ToggleTextDecoration(box, TextDecorationLocation.Strikethrough);
        Assert.True(((ParagraphBlock)Mapper.FromFlowDocument(box.Document).Blocks[0]).Runs.All(r => r.Format.HasFlag(RunFormat.Strikethrough)));
        box.SelectAll();
        EditorOperations.ToggleTextDecoration(box, TextDecorationLocation.Strikethrough);
        Assert.True(((ParagraphBlock)Mapper.FromFlowDocument(box.Document).Blocks[0]).Runs.All(r => !r.Format.HasFlag(RunFormat.Strikethrough)));
    });

    [Fact(DisplayName = "TC-EDT-05 Font size is limited to 8–72 points")]
    public void FontSizeClamp()
    {
        Assert.Equal(8, JournalEditor.ClampFontSize(7));
        Assert.Equal(72, JournalEditor.ClampFontSize(73));
        Assert.Equal(12, JournalEditor.ClampFontSize(12));
    }

    [Fact(DisplayName = "TC-EDT-09 Checklist indentation is limited to 4 levels; outdent stops at 0")]
    public void IndentLimits() => Sta.Run(() =>
    {
        var box = Box(new ParagraphBlock(new TextRun("task")) { List = ListKind.Checklist, ListLevel = 3 });
        var p = (WpfParagraph)box.Document.Blocks.FirstBlock;
        box.CaretPosition = p.ContentEnd;
        Assert.True(EditorOperations.Indent(box, 1));
        Assert.Equal(3, ((ParagraphBlock)Mapper.FromFlowDocument(box.Document).Blocks[0]).ListLevel);
        for (int i = 0; i < 5; i++) EditorOperations.Indent(box, -1);
        Assert.Equal(0, ((ParagraphBlock)Mapper.FromFlowDocument(box.Document).Blocks[0]).ListLevel);
        var plain = Box(ParagraphBlock.OfText("not a list"));
        plain.CaretPosition = plain.Document.ContentEnd;
        Assert.False(EditorOperations.Indent(plain, 1));
    });

    [Fact(DisplayName = "TC-EDT-08 Toggling a checklist adds and removes checkboxes")]
    public void ToggleChecklist() => Sta.Run(() =>
    {
        var box = Box(ParagraphBlock.OfText("a"), ParagraphBlock.OfText("b"));
        box.SelectAll();
        EditorOperations.ToggleChecklist(box);
        Assert.All(Mapper.FromFlowDocument(box.Document).Blocks.Cast<ParagraphBlock>(), p => Assert.Equal(ListKind.Checklist, p.List));
        box.SelectAll();
        EditorOperations.ToggleChecklist(box);
        Assert.All(Mapper.FromFlowDocument(box.Document).Blocks.Cast<ParagraphBlock>(), p => Assert.Equal(ListKind.None, p.List));
    });

    [Fact(DisplayName = "TC-EDT-12 Toolbar state reflects the formatting at the caret")]
    public void ReadFormatting() => Sta.Run(() =>
    {
        var box = Box(new ParagraphBlock(new TextRun("styled", RunFormat.Bold) { FontFamily = "Georgia", FontSize = 14, Foreground = ArgbColor.Parse("#FFC00000") }) { List = ListKind.Numbered });
        var list = (DocList)box.Document.Blocks.FirstBlock;
        var p = (WpfParagraph)list.ListItems.FirstListItem.Blocks.FirstBlock;
        box.Selection.Select(p.ContentStart.GetPositionAtOffset(2)!, p.ContentStart.GetPositionAtOffset(4)!);
        var f = EditorOperations.ReadFormatting(box);
        Assert.True(f.IsBold);
        Assert.Equal("Georgia", f.FontFamily);
        Assert.Equal(14, f.FontSizePt);
        Assert.Equal(Color.FromRgb(0xC0, 0, 0), f.Foreground);
        Assert.Equal(ListKind.Numbered, f.List);
    });

    [Fact(DisplayName = "Enter after a checklist item continues the checklist")]
    public void EnterContinuesChecklist() => Sta.Run(() =>
    {
        var box = Box(new ParagraphBlock(new TextRun("one")) { List = ListKind.Checklist, IsChecked = true });
        var first = (WpfParagraph)box.Document.Blocks.FirstBlock;
        box.CaretPosition = first.ContentEnd;
        var next = box.CaretPosition.InsertParagraphBreak();
        box.CaretPosition = next;
        EditorOperations.AfterEnter(box, first);
        var blocks = Mapper.FromFlowDocument(box.Document).Blocks.Cast<ParagraphBlock>().ToList();
        Assert.Equal(ListKind.Checklist, blocks[^1].List);
        Assert.False(blocks[^1].IsChecked);
    });
}
