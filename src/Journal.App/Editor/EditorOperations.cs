using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Journal.Core.Documents;
using DocList = System.Windows.Documents.List;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace Journal.App.Editor;

/// <summary>Editing operations on a RichTextBox that the built-in EditingCommands don't provide.</summary>
public static class EditorOperations
{
    /// <summary>
    /// Inserts a block (image or file) at the caret on its own line. A caret in the middle of a paragraph
    /// splits the paragraph around the block (FR-IMG-03).
    /// </summary>
    public static void InsertBlockAtCaret(RichTextBox box, Block block) => InsertBlocksAtCaret(box, [block]);

    /// <summary>
    /// Inserts blocks at the caret, in order, as <see cref="InsertBlockAtCaret"/> does for one block
    /// (also used to paste journal content, ISS-010). The caret ends up after the last block.
    /// </summary>
    public static void InsertBlocksAtCaret(RichTextBox box, IReadOnlyList<Block> blocks)
    {
        if (blocks.Count == 0) return;
        var doc = box.Document;
        var caret = box.Selection.IsEmpty ? box.CaretPosition : box.Selection.End;
        var paragraph = caret.Paragraph;
        var block = blocks[^1];

        void InsertAllBefore(BlockCollection siblings, Block anchor)
        {
            foreach (var b in blocks) siblings.InsertBefore(anchor, b);
        }

        void InsertAllAfter(BlockCollection siblings, Block anchor)
        {
            foreach (var b in blocks)
            {
                siblings.InsertAfter(anchor, b);
                anchor = b;
            }
        }

        if (paragraph is null)
        {
            foreach (var b in blocks) doc.Blocks.Add(b);
            doc.Blocks.Add(new WpfParagraph());
        }
        else
        {
            var atStart = caret.GetOffsetToPosition(ContentStartAfterCheckbox(paragraph)) >= 0;
            var atEnd = caret.GetOffsetToPosition(paragraph.ContentEnd) <= 0;
            var container = TopLevelBlock(paragraph);
            var siblings = container.SiblingBlocks!;
            if (atStart && !IsEmpty(paragraph))
            {
                InsertAllBefore(siblings, container);
            }
            else if (atEnd || IsEmpty(paragraph))
            {
                InsertAllAfter(siblings, container);
                if (block.NextBlock is null) siblings.Add(new WpfParagraph());
            }
            else
            {
                // Split: the text after the caret moves to a new paragraph after the block.
                var tail = new TextRange(caret, paragraph.ContentEnd);
                var tailParagraph = new WpfParagraph { TextAlignment = paragraph.TextAlignment };
                FlowDocumentMapper.ApplyStyle(tailParagraph, ParagraphProps.GetStyle(paragraph));
                using (var ms = new MemoryStream())
                {
                    tail.Save(ms, DataFormats.XamlPackage);
                    tail.Text = "";
                    ms.Position = 0;
                    new TextRange(tailParagraph.ContentStart, tailParagraph.ContentEnd).Load(ms, DataFormats.XamlPackage);
                }
                InsertAllAfter(siblings, container);
                siblings.InsertAfter(block, tailParagraph);
            }
        }
        var next = block.NextBlock ?? block;
        box.CaretPosition = next.ContentStart;
        box.Focus();
    }

    /// <summary>The top-level block (child of the document) that contains <paramref name="paragraph"/>.</summary>
    private static Block TopLevelBlock(WpfParagraph paragraph)
    {
        Block current = paragraph;
        while (current.Parent is not FlowDocument)
        {
            switch (current.Parent)
            {
                case ListItem { Parent: Block list }: current = list; break;
                case Block parent: current = parent; break;
                default: return current;
            }
        }
        return current;
    }

    private static TextPointer ContentStartAfterCheckbox(WpfParagraph p) =>
        FlowDocumentMapper.GetCheckBox(p) is not null ? p.Inlines.FirstInline.ElementEnd : p.ContentStart;

    public static bool IsEmpty(WpfParagraph p) =>
        new TextRange(p.ContentStart, p.ContentEnd).Text.Trim().Length == 0 && FlowDocumentMapper.GetCheckBox(p) is null;

    /// <summary>Paragraphs that overlap the selection (or contain the caret).</summary>
    public static IEnumerable<WpfParagraph> SelectedParagraphs(RichTextBox box)
    {
        var start = box.Selection.Start;
        var end = box.Selection.End;
        foreach (var p in AllParagraphs(box.Document.Blocks))
        {
            if (p.ContentEnd.CompareTo(start) < 0) continue;
            if (p.ContentStart.CompareTo(end) > 0) break;
            yield return p;
        }
    }

    public static IEnumerable<WpfParagraph> AllParagraphs(BlockCollection blocks)
    {
        foreach (var b in blocks)
        {
            switch (b)
            {
                case WpfParagraph p: yield return p; break;
                case DocList l:
                    foreach (var item in l.ListItems)
                        foreach (var p in AllParagraphs(item.Blocks)) yield return p;
                    break;
                case Section s:
                    foreach (var p in AllParagraphs(s.Blocks)) yield return p;
                    break;
            }
        }
    }

    /// <summary>Turns the selected paragraphs into checklist items, or back into normal paragraphs (FR-EDT-08).</summary>
    public static void ToggleChecklist(RichTextBox box)
    {
        var paragraphs = SelectedParagraphs(box).Where(p => p.Parent is FlowDocument or Section).ToList();
        if (paragraphs.Count == 0) return;
        bool makeChecklist = paragraphs.Any(p => FlowDocumentMapper.GetCheckBox(p) is null);
        box.BeginChange();
        try
        {
            foreach (var p in paragraphs)
            {
                var existing = FlowDocumentMapper.GetCheckBox(p);
                if (makeChecklist && existing is null)
                {
                    if (p.Inlines.FirstInline is null) p.Inlines.Add(FlowDocumentMapper.NewCheckBox(false));
                    else p.Inlines.InsertBefore(p.Inlines.FirstInline, FlowDocumentMapper.NewCheckBox(false));
                    p.Margin = new Thickness(0, 0, 0, 4);
                }
                else if (!makeChecklist && existing is not null)
                {
                    p.Inlines.Remove(p.Inlines.FirstInline);
                    p.ClearValue(Block.MarginProperty);
                    p.ClearValue(Inline.TextDecorationsProperty);
                }
            }
        }
        finally { box.EndChange(); }
    }

    /// <summary>Ticks or unticks the checklist item at the caret (Ctrl+Enter). Returns false when not in a checklist.</summary>
    public static bool ToggleChecked(RichTextBox box)
    {
        var p = box.CaretPosition.Paragraph;
        if (p is null || FlowDocumentMapper.GetCheckBox(p) is not { } cb) return false;
        cb.IsChecked = cb.IsChecked != true;
        ApplyCheckedDecoration(p, cb.IsChecked == true);
        return true;
    }

    /// <summary>Ticked items are shown struck through (FR-EDT-09). Display only; not stored in runs.</summary>
    public static void ApplyCheckedDecoration(WpfParagraph p, bool isChecked)
    {
        if (isChecked) p.TextDecorations = TextDecorations.Strikethrough;
        else p.ClearValue(Inline.TextDecorationsProperty);
    }

    /// <summary>Indents or outdents checklist items or list items, up to 4 levels (FR-EDT-07). Returns false when not applicable.</summary>
    public static bool Indent(RichTextBox box, int delta)
    {
        var p = box.CaretPosition.Paragraph;
        if (p is null) return false;
        if (FlowDocumentMapper.GetCheckBox(p) is not null)
        {
            int level = (int)Math.Round(p.Margin.Left / FlowDocumentMapper.IndentStep) + delta;
            level = Math.Clamp(level, 0, ParagraphBlock.MaxListLevel);
            p.Margin = new Thickness(level * FlowDocumentMapper.IndentStep, 0, 0, 4);
            return true;
        }
        if (p.Parent is ListItem)
        {
            if (delta > 0 && ListDepth(p) > ParagraphBlock.MaxListLevel) return true;
            if (delta > 0) EditingCommands.IncreaseIndentation.Execute(null, box);
            else EditingCommands.DecreaseIndentation.Execute(null, box);
            return true;
        }
        return false;
    }

    /// <summary>Number of lists that contain the paragraph (1 for a top-level list item).</summary>
    public static int ListDepth(WpfParagraph p)
    {
        int depth = 0;
        for (DependencyObject? d = p; d is TextElement te; d = te.Parent)
            if (d is DocList) depth++;
        return depth;
    }

    public static void SetParagraphStyle(RichTextBox box, ParagraphStyle style)
    {
        box.BeginChange();
        try
        {
            foreach (var p in SelectedParagraphs(box))
            {
                bool isChecklist = FlowDocumentMapper.GetCheckBox(p) is not null;
                var margin = p.Margin;
                FlowDocumentMapper.ApplyStyle(p, style);
                if (isChecklist) p.Margin = margin;
            }
        }
        finally { box.EndChange(); }
    }

    public static void ToggleTextDecoration(RichTextBox box, TextDecorationLocation location)
    {
        var selection = box.Selection;
        var current = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        bool has = current?.Any(d => d.Location == location) == true;
        var updated = new TextDecorationCollection(current?.Where(d => d.Location != location) ?? []);
        if (!has) updated.Add(location == TextDecorationLocation.Strikethrough ? TextDecorations.Strikethrough[0] : TextDecorations.Underline[0]);
        selection.ApplyPropertyValue(Inline.TextDecorationsProperty, updated);
    }

    public static void ApplyColor(RichTextBox box, DependencyProperty property, Color? color)
    {
        if (color is { } c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            box.Selection.ApplyPropertyValue(property, brush);
        }
        else
        {
            // Reset to default: clear local values on the runs in the selection.
            foreach (var run in RunsIn(box.Selection)) run.ClearValue(property);
            if (property == TextElement.ForegroundProperty) box.Selection.ApplyPropertyValue(property, box.Foreground);
            foreach (var run in RunsIn(box.Selection)) run.ClearValue(property);
        }
    }

    private static IEnumerable<Run> RunsIn(TextSelection selection)
    {
        var runs = new List<Run>();
        for (var pos = selection.Start; pos is not null && pos.CompareTo(selection.End) < 0; pos = pos.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (pos.Parent is Run r && !runs.Contains(r)) runs.Add(r);
        }
        return runs;
    }

    /// <summary>
    /// Called after Enter: if the previous paragraph was a checklist item, give the new paragraph a checkbox;
    /// a heading's following paragraph goes back to Normal.
    /// </summary>
    public static void AfterEnter(RichTextBox box, WpfParagraph? previous)
    {
        var current = box.CaretPosition.Paragraph;
        if (current is null || previous is null || ReferenceEquals(current, previous)) return;
        if (ParagraphProps.GetStyle(current) != ParagraphStyle.Normal && IsEmpty(current))
            FlowDocumentMapper.ApplyStyle(current, ParagraphStyle.Normal);
        if (FlowDocumentMapper.GetCheckBox(previous) is not null && FlowDocumentMapper.GetCheckBox(current) is null)
        {
            if (current.Inlines.FirstInline is null) current.Inlines.Add(FlowDocumentMapper.NewCheckBox(false));
            else current.Inlines.InsertBefore(current.Inlines.FirstInline, FlowDocumentMapper.NewCheckBox(false));
            current.Margin = previous.Margin;
            current.ClearValue(Inline.TextDecorationsProperty);
            box.CaretPosition = current.Inlines.FirstInline!.ElementEnd;
        }
        else if (FlowDocumentMapper.GetCheckBox(current) is { } cb)
        {
            // Text split off a ticked item starts unticked.
            cb.IsChecked = false;
            ApplyCheckedDecoration(current, false);
        }
    }

    /// <summary>True when the caret is in an empty checklist item (Enter there ends the checklist).</summary>
    public static bool IsInEmptyChecklistItem(RichTextBox box)
    {
        var p = box.CaretPosition.Paragraph;
        return p is not null && FlowDocumentMapper.GetCheckBox(p) is not null &&
               new TextRange(p.Inlines.FirstInline.ElementEnd, p.ContentEnd).Text.Trim().Length == 0;
    }

    public static void RemoveCheckbox(WpfParagraph p)
    {
        if (FlowDocumentMapper.GetCheckBox(p) is null) return;
        p.Inlines.Remove(p.Inlines.FirstInline);
        p.ClearValue(Block.MarginProperty);
        p.ClearValue(Inline.TextDecorationsProperty);
    }

    /// <summary>Removes the BlockUIContainer that hosts <paramref name="element"/>.</summary>
    public static bool RemoveHostedBlock(FrameworkElement element)
    {
        if (element.Parent is BlockUIContainer container && container.SiblingBlocks is { } siblings)
        {
            siblings.Remove(container);
            return true;
        }
        return false;
    }

    /// <summary>True when <paramref name="element"/> is hosted inside <paramref name="document"/>.</summary>
    public static bool IsInDocument(DependencyObject element, FlowDocument document)
    {
        for (DependencyObject? d = element; d is not null; d = LogicalTreeHelper.GetParent(d) ?? (d is FrameworkElement fe ? fe.Parent : null))
        {
            if (ReferenceEquals(d, document)) return true;
            if (d is FlowDocument) return false;
        }
        return false;
    }

    /// <summary>Formatting at the caret or selection, for the toolbar (FR-EDT-10). Mixed values are null.</summary>
    public static FormattingState ReadFormatting(RichTextBox box)
    {
        var sel = box.Selection;
        var weight = sel.GetPropertyValue(TextElement.FontWeightProperty);
        var style = sel.GetPropertyValue(TextElement.FontStyleProperty);
        var family = sel.GetPropertyValue(TextElement.FontFamilyProperty);
        var size = sel.GetPropertyValue(TextElement.FontSizeProperty);
        var fg = sel.GetPropertyValue(TextElement.ForegroundProperty);
        var decorations = sel.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        var p = sel.Start.Paragraph;
        var list = ListKind.None;
        if (p is not null)
        {
            if (FlowDocumentMapper.GetCheckBox(p) is not null) list = ListKind.Checklist;
            else if (p.Parent is ListItem { Parent: DocList l })
                list = l.MarkerStyle == TextMarkerStyle.Decimal ? ListKind.Numbered : ListKind.Bulleted;
        }
        return new FormattingState(
            IsBold: weight is FontWeight w && w.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight(),
            IsItalic: style is FontStyle fs && fs == FontStyles.Italic,
            IsUnderline: decorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true,
            IsStrikethrough: decorations?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true,
            FontFamily: family is FontFamily ff ? ff.Source : null,
            FontSizePt: size is double dip ? FlowDocumentMapper.DipToPt(dip) : null,
            Foreground: fg is SolidColorBrush b ? b.Color : null,
            List: list);
    }
}

public sealed record FormattingState(
    bool IsBold, bool IsItalic, bool IsUnderline, bool IsStrikethrough,
    string? FontFamily, double? FontSizePt, Color? Foreground, ListKind List);
