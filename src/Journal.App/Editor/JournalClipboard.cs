using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Journal.Core.Documents;
using Journal.Core.Persistence;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace Journal.App.Editor;

/// <summary>
/// Copy, cut, paste and drag within the journal keep everything the editor can hold (ISS-010, FR-EDT-17).
/// WPF's own copy leaves out embedded controls (checkboxes, images, file chips) and the journal's heading
/// marker, so the editor also puts the selection on the clipboard in the journal's document format and reads
/// that format back on paste. The standard text, RTF and XAML formats stay, so other apps still get text.
/// </summary>
public static class JournalClipboard
{
    /// <summary>Clipboard format holding a <see cref="JournalDocument"/> fragment as JSON.</summary>
    public const string Format = "Journal.DocumentFragment.v1";

    private static readonly FlowDocumentMapper Mapper = new();

    /// <summary>Adds the journal format when the editor copies, cuts or starts a drag, and reads it on paste and drop.</summary>
    public static void Attach(RichTextBox box, Func<bool> canPaste, Action pasted)
    {
        DataObject.AddCopyingHandler(box, (_, e) =>
        {
            if (Fragment(box) is { } fragment) AddTo(e.DataObject, fragment);
        });
        DataObject.AddPastingHandler(box, (_, e) =>
        {
            if (!canPaste() || !e.DataObject.GetDataPresent(Format) || Read(e.DataObject) is not { } fragment) return;
            Insert(box, fragment);
            if (e.IsDragDrop)
            {
                // Let the drop finish with nothing to paste, so a move still removes the dragged original.
                e.DataObject = new DataObject(DataFormats.UnicodeText, "");
                e.FormatToApply = DataFormats.UnicodeText;
            }
            else
            {
                e.CancelCommand();
            }
            pasted();
        });
    }

    /// <summary>
    /// The selection as journal content, or null when WPF's own copy already keeps it: an empty selection,
    /// or text inside one plain paragraph.
    /// </summary>
    public static JournalDocument? Fragment(RichTextBox box)
    {
        var selection = box.Selection;
        if (selection.IsEmpty) return null;
        var fragment = Mapper.FromRange(box.Document, selection.Start, selection.End);
        if (fragment.Blocks.Count == 0) return null;
        if (fragment.Blocks is [ParagraphBlock only] && !NeedsJournalFormat(only, selection)) return null;
        return fragment;
    }

    /// <summary>A single paragraph needs the journal format only if it is wholly selected and is a heading or list item.</summary>
    private static bool NeedsJournalFormat(ParagraphBlock p, TextSelection selection)
    {
        if (p.Style == ParagraphStyle.Normal && p.List == ListKind.None) return false;
        var whole = selection.Start.Paragraph is { } para
            ? FlowDocumentMapper.ReadParagraph(para).PlainText
            : p.PlainText;
        return p.PlainText == whole;
    }

    /// <summary>Puts the fragment on <paramref name="data"/> and replaces the plain text with a version that keeps ticks.</summary>
    public static void AddTo(IDataObject data, JournalDocument fragment)
    {
        data.SetData(Format, JournalJson.SerializeDocument(fragment));
        var text = PlainText(fragment);
        data.SetData(DataFormats.UnicodeText, text);
        data.SetData(DataFormats.Text, text);
    }

    public static JournalDocument? Read(IDataObject data)
    {
        try
        {
            return data.GetData(Format) is string json ? JournalJson.DeserializeDocument(json) : null;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Text for other apps: one line per paragraph, checklist items as "☐ item" or "☑ item".</summary>
    public static string PlainText(JournalDocument fragment)
    {
        var sb = new StringBuilder();
        foreach (var p in fragment.Paragraphs())
        {
            if (sb.Length > 0) sb.Append(Environment.NewLine);
            if (p.List == ListKind.Checklist)
                sb.Append(new string(' ', 2 * p.ListLevel)).Append(p.IsChecked ? "☑ " : "☐ ");
            sb.Append(p.PlainText.Replace("\n", Environment.NewLine));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Inserts the fragment at the caret, replacing any selection, as one undo step. Images and files keep
    /// pointing to the same encrypted file in the vault, so nothing is copied or lost. New block ids keep
    /// pasted blocks distinct from the originals.
    /// </summary>
    public static void Insert(RichTextBox box, JournalDocument fragment)
    {
        foreach (var b in fragment.Blocks) b.Id = Guid.NewGuid();
        box.BeginChange();
        try
        {
            if (!box.Selection.IsEmpty) box.Selection.Text = "";
            var target = box.CaretPosition.Paragraph;
            var replaceEmptyLine = target is { Parent: FlowDocument } && EditorOperations.IsEmpty(target);
            var flow = Mapper.ToFlowDocument(fragment);
            var blocks = flow.Blocks.ToList();
            flow.Blocks.Clear();
            EditorOperations.InsertBlocksAtCaret(box, blocks);
            // Pasting on an empty line uses that line instead of leaving it above the pasted content.
            if (replaceEmptyLine && target!.Parent is FlowDocument doc) doc.Blocks.Remove(target);
        }
        finally
        {
            box.EndChange();
        }
    }
}
