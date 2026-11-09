using System.Text.Json.Serialization;

namespace Journal.Core.Documents;

/// <summary>Reference to an encrypted blob (image or file) in the vault.</summary>
public sealed record BlobRef(Guid Id, string Sha256);

/// <summary>
/// Visitor over the block kinds. Code that handles every kind implements this, so adding a kind won't compile
/// until it is handled (ISS-020).
/// </summary>
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

    /// <summary>True when the block holds nothing worth keeping.</summary>
    public abstract bool IsEmpty();

    /// <summary>The encrypted vault files this block uses.</summary>
    public virtual IEnumerable<BlobRef> ReferencedBlobs() => [];
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

    public override bool IsEmpty() => string.IsNullOrWhiteSpace(PlainText);
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

    public override bool IsEmpty() => false;

    public override IEnumerable<BlobRef> ReferencedBlobs() => [Image];
}

public sealed class AttachmentBlock : DocBlock
{
    public required BlobRef File { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Description { get; set; } = "";

    public override void Accept(IDocBlockVisitor visitor) => visitor.Visit(this);

    public override bool IsEmpty() => false;

    public override IEnumerable<BlobRef> ReferencedBlobs() => [File];

    /// <summary>Human readable size such as "24 KB".</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{Math.Round(bytes / 1024.0)} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };
}
