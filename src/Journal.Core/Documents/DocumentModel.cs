using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Journal.Core.Persistence;

namespace Journal.Core.Documents;

/// <summary>Bold / italic / underline / strikethrough flags for a run of text.</summary>
[Flags]
public enum RunFormat
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikethrough = 8,
}

public enum ParagraphStyle { Normal, Heading1, Heading2 }

public enum ListKind { None, Bulleted, Numbered, Checklist }

public enum TextAlign { Left, Center, Right, Justify }

/// <summary>An ARGB color stored as a value object and serialized as "#AARRGGBB".</summary>
[JsonConverter(typeof(ArgbColorJsonConverter))]
public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
    public string ToHex() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public override string ToString() => ToHex();

    public static ArgbColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public static bool TryParse(string? text, out ArgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return false;
        color = new ArgbColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static ArgbColor Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"'{text}' is not a color in #AARRGGBB or #RRGGBB form.");
}

/// <summary>Reference to an encrypted blob (image or file) in the vault.</summary>
public sealed record BlobRef(Guid Id, string Sha256);

/// <summary>A run of text with uniform formatting. Null font, size or color means "use the default".</summary>
public sealed record TextRun
{
    public string Text { get; init; } = "";
    public RunFormat Format { get; init; }
    public string? FontFamily { get; init; }
    /// <summary>Font size in points.</summary>
    public double? FontSize { get; init; }
    public ArgbColor? Foreground { get; init; }
    public ArgbColor? Highlight { get; init; }

    public TextRun() { }

    public TextRun(string text, RunFormat format = RunFormat.None)
    {
        Text = text;
        Format = format;
    }

    /// <summary>True when the two runs differ only in their text.</summary>
    public bool HasSameFormatting(TextRun other) =>
        Format == other.Format && FontFamily == other.FontFamily && FontSize == other.FontSize &&
        Foreground == other.Foreground && Highlight == other.Highlight;
}

/// <summary>Visitor over the block hierarchy (text extraction, mapping, blob collection).</summary>
public interface IDocBlockVisitor
{
    void Visit(ParagraphBlock paragraph);
    void Visit(ImageBlock image);
    void Visit(AttachmentBlock attachment);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$t")]
[JsonDerivedType(typeof(ParagraphBlock), "p")]
[JsonDerivedType(typeof(ImageBlock), "img")]
[JsonDerivedType(typeof(AttachmentBlock), "file")]
public abstract class DocBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public abstract void Accept(IDocBlockVisitor visitor);
}

public sealed class ParagraphBlock : DocBlock
{
    public ParagraphStyle Style { get; set; }
    public ListKind List { get; set; }
    /// <summary>Nesting level 0..3 for list items.</summary>
    public int ListLevel { get; set; }
    /// <summary>Only meaningful when <see cref="List"/> is <see cref="ListKind.Checklist"/>.</summary>
    public bool IsChecked { get; set; }
    public TextAlign Alignment { get; set; }
    public List<TextRun> Runs { get; set; } = [];

    public const int MaxListLevel = 3;

    public ParagraphBlock() { }

    public ParagraphBlock(params TextRun[] runs) => Runs = [.. runs];

    public static ParagraphBlock OfText(string text) => new(new TextRun(text));

    [JsonIgnore]
    public string PlainText => string.Concat(Runs.Select(r => r.Text));

    [JsonIgnore]
    public bool IsChecklistItem => List == ListKind.Checklist;

    /// <summary>Merges adjacent runs with identical formatting and drops empty runs.</summary>
    public ParagraphBlock NormalizeRuns()
    {
        var merged = new List<TextRun>();
        foreach (var run in Runs)
        {
            if (run.Text.Length == 0) continue;
            if (merged.Count > 0 && merged[^1].HasSameFormatting(run))
                merged[^1] = merged[^1] with { Text = merged[^1].Text + run.Text };
            else
                merged.Add(run);
        }
        Runs = merged;
        return this;
    }

    public override void Accept(IDocBlockVisitor visitor) => visitor.Visit(this);
}

public sealed class ImageBlock : DocBlock
{
    public required BlobRef Image { get; set; }
    public string Caption { get; set; } = "";
    /// <summary>Display width in device-independent pixels; null means fit to editor width.</summary>
    public double? DisplayWidth { get; set; }
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }

    public override void Accept(IDocBlockVisitor visitor) => visitor.Visit(this);
}

public sealed class AttachmentBlock : DocBlock
{
    public required BlobRef File { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Description { get; set; } = "";

    public override void Accept(IDocBlockVisitor visitor) => visitor.Visit(this);

    /// <summary>Human readable size such as "24 KB".</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{Math.Round(bytes / 1024.0)} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };
}

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
    public bool IsEmpty() =>
        Blocks.All(b => b is ParagraphBlock p && string.IsNullOrWhiteSpace(p.PlainText));

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

    public IEnumerable<BlobRef> ReferencedBlobs()
    {
        foreach (var b in Blocks)
        {
            if (b is ImageBlock i) yield return i.Image;
            else if (b is AttachmentBlock a) yield return a.File;
        }
    }
}
