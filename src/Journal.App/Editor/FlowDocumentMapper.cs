using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Journal.App.Controls;
using Journal.Core.Documents;
using DocList = System.Windows.Documents.List;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace Journal.App.Editor;

/// <summary>Attached property that marks a paragraph's style (heading levels) in the FlowDocument.</summary>
public static class ParagraphProps
{
    public static readonly DependencyProperty StyleProperty = DependencyProperty.RegisterAttached(
        "Style", typeof(ParagraphStyle), typeof(ParagraphProps), new PropertyMetadata(ParagraphStyle.Normal));

    public static ParagraphStyle GetStyle(DependencyObject d) => (ParagraphStyle)d.GetValue(StyleProperty);
    public static void SetStyle(DependencyObject d, ParagraphStyle value) => d.SetValue(StyleProperty, value);
}

/// <summary>
/// Converts between the stored document model and a WPF FlowDocument (Design §9). The stored format never
/// contains XAML; images and files are BlockUIContainers hosting <see cref="ImageBlockView"/> and
/// <see cref="AttachmentChip"/>; checklist items are paragraphs whose first inline is a CheckBox.
/// </summary>
public sealed class FlowDocumentMapper
{
    public const double DefaultFontSizePt = 11;
    public const string DefaultFontFamily = "Segoe UI";
    public const double IndentStep = 24;
    public static readonly (double Size, FontWeight Weight) Heading1 = (18, FontWeights.SemiBold);
    public static readonly (double Size, FontWeight Weight) Heading2 = (14, FontWeights.SemiBold);

    public static double PtToDip(double pt) => pt * 96.0 / 72.0;
    public static double DipToPt(double dip) => Math.Round(dip * 72.0 / 96.0, 1);

    // ------------------------------------------------------------------ model -> FlowDocument

    public FlowDocument ToFlowDocument(JournalDocument doc)
    {
        var fd = CreateEmptyDocument();
        var stack = new List<(DocList List, ListKind Kind)>();

        foreach (var block in doc.Blocks)
        {
            if (block is ParagraphBlock { List: ListKind.Bulleted or ListKind.Numbered } p)
            {
                int level = Math.Clamp(p.ListLevel, 0, Math.Min(stack.Count, ParagraphBlock.MaxListLevel));
                while (stack.Count > level + 1) stack.RemoveAt(stack.Count - 1);
                if (stack.Count == level + 1 && stack[^1].Kind != p.List) stack.RemoveAt(stack.Count - 1);
                if (stack.Count == level)
                {
                    var list = NewList(p.List);
                    if (level == 0) fd.Blocks.Add(list);
                    else
                    {
                        var parent = stack[^1].List;
                        var host = parent.ListItems.LastListItem ?? AddItem(parent, new WpfParagraph());
                        host.Blocks.Add(list);
                    }
                    stack.Add((list, p.List));
                }
                AddItem(stack[^1].List, BuildParagraph(p));
                continue;
            }

            stack.Clear();
            fd.Blocks.Add(block switch
            {
                ParagraphBlock para => BuildParagraph(para),
                ImageBlock img => ImageContainer(img),
                AttachmentBlock att => AttachmentContainer(att),
                _ => new WpfParagraph(),
            });
        }
        if (fd.Blocks.Count == 0) fd.Blocks.Add(new WpfParagraph());
        return fd;
    }

    public static FlowDocument CreateEmptyDocument()
    {
        var fd = new FlowDocument
        {
            FontFamily = new FontFamily(DefaultFontFamily),
            FontSize = PtToDip(DefaultFontSizePt),
            PagePadding = new Thickness(16, 12, 16, 12),
            TextAlignment = TextAlignment.Left,
        };
        var paragraphStyle = new Style(typeof(WpfParagraph));
        paragraphStyle.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0, 0, 0, 6)));
        fd.Resources.Add(typeof(WpfParagraph), paragraphStyle);
        var listStyle = new Style(typeof(DocList));
        listStyle.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0, 0, 0, 6)));
        listStyle.Setters.Add(new Setter(DocList.PaddingProperty, new Thickness(IndentStep, 0, 0, 0)));
        fd.Resources.Add(typeof(DocList), listStyle);
        return fd;
    }

    public static DocList NewList(ListKind kind) => new()
    {
        MarkerStyle = kind == ListKind.Numbered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
    };

    private static ListItem AddItem(DocList list, WpfParagraph p)
    {
        var item = new ListItem(p);
        list.ListItems.Add(item);
        return item;
    }

    public static BlockUIContainer ImageContainer(ImageBlock img) =>
        new(ImageBlockView.From(img)) { Margin = new Thickness(0, 4, 0, 10) };

    public static BlockUIContainer AttachmentContainer(AttachmentBlock att) =>
        new(AttachmentChip.From(att)) { Margin = new Thickness(0, 2, 0, 8) };

    public static WpfParagraph BuildParagraph(ParagraphBlock p)
    {
        var para = new WpfParagraph { TextAlignment = ToWpf(p.Alignment) };
        ApplyStyle(para, p.Style);
        if (p.List == ListKind.Checklist)
        {
            para.Inlines.Add(NewCheckBox(p.IsChecked));
            para.Margin = new Thickness(IndentStep * Math.Clamp(p.ListLevel, 0, ParagraphBlock.MaxListLevel), 0, 0, 4);
            if (p.IsChecked) para.TextDecorations = TextDecorations.Strikethrough;
        }
        foreach (var run in p.Runs) AddRun(para, run);
        return para;
    }

    public static void ApplyStyle(WpfParagraph para, ParagraphStyle style)
    {
        ParagraphProps.SetStyle(para, style);
        switch (style)
        {
            case ParagraphStyle.Heading1:
                para.FontSize = PtToDip(Heading1.Size);
                para.FontWeight = Heading1.Weight;
                para.Margin = new Thickness(0, 10, 0, 6);
                break;
            case ParagraphStyle.Heading2:
                para.FontSize = PtToDip(Heading2.Size);
                para.FontWeight = Heading2.Weight;
                para.Margin = new Thickness(0, 8, 0, 4);
                break;
            default:
                para.ClearValue(TextElement.FontSizeProperty);
                para.ClearValue(TextElement.FontWeightProperty);
                para.ClearValue(Block.MarginProperty);
                break;
        }
    }

    public static InlineUIContainer NewCheckBox(bool isChecked) => new(new ChecklistCheckBox
    {
        IsChecked = isChecked,
        Focusable = false,
        Cursor = System.Windows.Input.Cursors.Arrow,
    })
    {
        BaselineAlignment = BaselineAlignment.Center,
    };

    private static void AddRun(WpfParagraph para, TextRun run)
    {
        var lines = run.Text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) para.Inlines.Add(new LineBreak());
            if (lines[i].Length > 0) para.Inlines.Add(BuildRun(lines[i], run));
        }
    }

    private static Run BuildRun(string text, TextRun run)
    {
        var r = new Run(text);
        if (run.Format.HasFlag(RunFormat.Bold)) r.FontWeight = FontWeights.Bold;
        if (run.Format.HasFlag(RunFormat.Italic)) r.FontStyle = FontStyles.Italic;
        var decorations = new TextDecorationCollection();
        if (run.Format.HasFlag(RunFormat.Underline)) decorations.Add(TextDecorations.Underline);
        if (run.Format.HasFlag(RunFormat.Strikethrough)) decorations.Add(TextDecorations.Strikethrough);
        if (decorations.Count > 0) r.TextDecorations = decorations;
        if (run.FontFamily is { } ff) r.FontFamily = new FontFamily(ff);
        if (run.FontSize is { } size) r.FontSize = PtToDip(size);
        if (run.Foreground is { } fg) r.Foreground = Brush(fg);
        if (run.Highlight is { } hl) r.Background = Brush(hl);
        return r;
    }

    private static SolidColorBrush Brush(ArgbColor c)
    {
        var b = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    private static TextAlignment ToWpf(TextAlign a) => a switch
    {
        TextAlign.Center => TextAlignment.Center,
        TextAlign.Right => TextAlignment.Right,
        TextAlign.Justify => TextAlignment.Justify,
        _ => TextAlignment.Left,
    };

    private static TextAlign FromWpf(TextAlignment a) => a switch
    {
        TextAlignment.Center => TextAlign.Center,
        TextAlignment.Right => TextAlign.Right,
        TextAlignment.Justify => TextAlign.Justify,
        _ => TextAlign.Left,
    };

    // ------------------------------------------------------------------ FlowDocument -> model

    public JournalDocument FromFlowDocument(FlowDocument fd)
    {
        var blocks = new List<DocBlock>();
        Walk(fd.Blocks, -1, ListKind.None, blocks, null);
        // Drop trailing empty paragraphs the editor always keeps, but keep one when the document is empty.
        while (blocks.Count > 0 && blocks[^1] is ParagraphBlock { List: ListKind.None } last && last.Runs.Count == 0 && blocks.Count > 1)
            blocks.RemoveAt(blocks.Count - 1);
        if (blocks.Count == 1 && blocks[0] is ParagraphBlock { Runs.Count: 0, List: ListKind.None }) blocks.Clear();
        return new JournalDocument(blocks);
    }

    /// <summary>
    /// The blocks that overlap [start, end), with paragraphs cut to the selected text but keeping their
    /// style, list and checkbox (ISS-010: copying and pasting within the journal).
    /// </summary>
    public JournalDocument FromRange(FlowDocument fd, TextPointer start, TextPointer end)
    {
        var blocks = new List<DocBlock>();
        Walk(fd.Blocks, -1, ListKind.None, blocks, (start, end));
        return new JournalDocument(blocks);
    }

    private void Walk(BlockCollection source, int listLevel, ListKind kind, List<DocBlock> output, (TextPointer Start, TextPointer End)? range)
    {
        foreach (var block in source)
        {
            if (range is { } r && (block.ElementEnd.CompareTo(r.Start) <= 0 || block.ElementStart.CompareTo(r.End) >= 0)) continue;
            switch (block)
            {
                case WpfParagraph p:
                    output.Add(ReadParagraph(p, kind, Math.Max(listLevel, 0), range));
                    break;
                case DocList list:
                    var listKind = IsNumbered(list.MarkerStyle) ? ListKind.Numbered : ListKind.Bulleted;
                    int level = Math.Min(listLevel + 1, ParagraphBlock.MaxListLevel);
                    foreach (var item in list.ListItems) Walk(item.Blocks, level, listKind, output, range);
                    break;
                case BlockUIContainer { Child: ImageBlockView view }:
                    output.Add(view.ToBlock());
                    break;
                case BlockUIContainer { Child: AttachmentChip chip }:
                    output.Add(chip.ToBlock());
                    break;
                case Section section:
                    Walk(section.Blocks, listLevel, kind, output, range);
                    break;
                case Table table:
                    foreach (var group in table.RowGroups)
                        foreach (var row in group.Rows)
                            foreach (var cell in row.Cells)
                                Walk(cell.Blocks, -1, ListKind.None, output, range);
                    break;
            }
        }
    }

    private static bool IsNumbered(TextMarkerStyle s) =>
        s is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin
            or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman;

    public static ParagraphBlock ReadParagraph(WpfParagraph p, ListKind kind = ListKind.None, int level = 0,
        (TextPointer Start, TextPointer End)? range = null)
    {
        var result = new ParagraphBlock
        {
            Style = ParagraphProps.GetStyle(p),
            Alignment = FromWpf(p.TextAlignment),
        };
        var inlines = p.Inlines.ToList();
        if (GetCheckBox(p) is { } cb)
        {
            result.List = ListKind.Checklist;
            result.IsChecked = cb.IsChecked == true;
            result.ListLevel = Math.Clamp((int)Math.Round(p.Margin.Left / IndentStep), 0, ParagraphBlock.MaxListLevel);
            inlines.RemoveAt(0);
        }
        else if (kind != ListKind.None)
        {
            result.List = kind;
            result.ListLevel = level;
        }
        foreach (var inline in inlines) ReadInline(inline, result.Runs, range);
        return result.NormalizeRuns();
    }

    /// <summary>The checklist checkbox when the paragraph's first inline is one.</summary>
    public static CheckBox? GetCheckBox(WpfParagraph p) =>
        p.Inlines.FirstInline is InlineUIContainer { Child: CheckBox cb } ? cb : null;

    /// <summary>Reads an inline's text; with a range, only the part inside it.</summary>
    private static void ReadInline(Inline inline, List<TextRun> runs, (TextPointer Start, TextPointer End)? range)
    {
        switch (inline)
        {
            case Run r:
                var text = r.Text;
                if (range is { } rr)
                {
                    var from = Max(r.ContentStart, rr.Start);
                    var to = Min(r.ContentEnd, rr.End);
                    // Cut the run's own text by offset; TextRange.Text would add list bullets ("•\t").
                    var skip = r.ContentStart.GetOffsetToPosition(from);
                    text = from.CompareTo(to) < 0 ? text.Substring(skip, from.GetOffsetToPosition(to)) : "";
                }
                if (text.Length > 0) runs.Add(ReadRunFormat(r) with { Text = text });
                break;
            case LineBreak lb:
                if (range is not { } lr || (lb.ElementStart.CompareTo(lr.Start) >= 0 && lb.ElementEnd.CompareTo(lr.End) <= 0))
                    runs.Add(ReadRunFormat(lb) with { Text = "\n" });
                break;
            case Span span:
                foreach (var child in span.Inlines.ToList()) ReadInline(child, runs, range);
                break;
        }
    }

    private static TextPointer Max(TextPointer a, TextPointer b) => a.CompareTo(b) >= 0 ? a : b;
    private static TextPointer Min(TextPointer a, TextPointer b) => a.CompareTo(b) <= 0 ? a : b;

    /// <summary>
    /// Resolves formatting from local values on the inline and its parent spans (stopping at the paragraph),
    /// so paragraph-level styling such as headings and ticked-checklist strikethrough is not copied into runs.
    /// </summary>
    private static TextRun ReadRunFormat(TextElement element)
    {
        var format = RunFormat.None;
        string? family = null;
        double? size = null;
        ArgbColor? fg = null, hl = null;
        bool weightSet = false, styleSet = false;

        for (DependencyObject? d = element; d is Inline inline; d = inline.Parent)
        {
            if (!weightSet && inline.ReadLocalValue(TextElement.FontWeightProperty) is FontWeight w)
            {
                weightSet = true;
                if (w.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight()) format |= RunFormat.Bold;
            }
            if (!weightSet && inline is Bold) { weightSet = true; format |= RunFormat.Bold; }
            if (!styleSet && inline.ReadLocalValue(TextElement.FontStyleProperty) is FontStyle s)
            {
                styleSet = true;
                if (s == FontStyles.Italic || s == FontStyles.Oblique) format |= RunFormat.Italic;
            }
            if (!styleSet && inline is Italic) { styleSet = true; format |= RunFormat.Italic; }
            if (inline is Underline) format |= RunFormat.Underline;
            if (inline.ReadLocalValue(Inline.TextDecorationsProperty) is TextDecorationCollection decorations)
            {
                foreach (var dec in decorations)
                {
                    if (dec.Location == TextDecorationLocation.Underline) format |= RunFormat.Underline;
                    if (dec.Location == TextDecorationLocation.Strikethrough) format |= RunFormat.Strikethrough;
                }
            }
            family ??= inline.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily ff ? ff.Source : null;
            size ??= inline.ReadLocalValue(TextElement.FontSizeProperty) is double dip ? DipToPt(dip) : null;
            fg ??= inline.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush fb ? ToArgb(fb.Color) : null;
            hl ??= inline.ReadLocalValue(TextElement.BackgroundProperty) is SolidColorBrush bb ? ToArgb(bb.Color) : null;
        }
        if (family == DefaultFontFamily) family = null;
        return new TextRun { Format = format, FontFamily = family, FontSize = size, Foreground = fg, Highlight = hl };
    }

    private static ArgbColor ToArgb(Color c) => new(c.A, c.R, c.G, c.B);
}
