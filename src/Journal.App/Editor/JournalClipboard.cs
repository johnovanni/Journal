using System.Text;
using System.Windows;
using Journal.Core.Documents;
using Journal.Core.Persistence;

namespace Journal.App.Editor;

/// <summary>
/// Copy, cut, paste and drag within the journal keep everything the editor can hold (ISS-010, FR-EDT-13).
/// WPF's own copy leaves out embedded controls (checkboxes, images, file chips) and the journal's heading
/// marker, so the editor also puts the selection on the clipboard in the journal's document format and reads
/// that format back on paste. The standard text, RTF and XAML formats stay, so other apps still get text.
/// This class is the clipboard format; <see cref="JournalDocumentEditor"/> does the copying and pasting.
/// </summary>
public static class JournalClipboard
{
    /// <summary>Clipboard format holding a <see cref="JournalDocument"/> fragment as JSON.</summary>
    public const string Format = "Journal.DocumentFragment.v1";

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
}
