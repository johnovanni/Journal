using System.Text;

namespace Journal.Core.Documents;

/// <summary>The content of one tab: an ordered list of blocks.</summary>
public sealed class JournalDocument
{
    public List<DocBlock> Blocks { get; set; } = [];

    public JournalDocument() { }

    public JournalDocument(IEnumerable<DocBlock> blocks) => Blocks = [.. blocks];

    public static JournalDocument Empty() => new();

    public IEnumerable<ImageBlock> Images() => Blocks.OfType<ImageBlock>();

    public IEnumerable<AttachmentBlock> Attachments() => Blocks.OfType<AttachmentBlock>();

    public IEnumerable<ParagraphBlock> Paragraphs() => Blocks.OfType<ParagraphBlock>();

    /// <summary>True when there is no text, image or file.</summary>
    public bool IsEmpty() => Blocks.All(b => b.IsEmpty());

    public string ExtractPlainText()
    {
        var sb = new StringBuilder();
        foreach (var p in Paragraphs())
        {
            var text = p.PlainText;
            if (text.Length == 0) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(text);
        }
        return sb.ToString();
    }

    public (int Open, int Done) ChecklistCounts()
    {
        int open = 0, done = 0;
        foreach (var p in Paragraphs().Where(p => p.IsChecklistItem && p.PlainText.Trim().Length > 0))
        {
            if (p.IsChecked) done++; else open++;
        }
        return (open, done);
    }

    public IEnumerable<BlobRef> ReferencedBlobs() => Blocks.SelectMany(b => b.ReferencedBlobs());
}
