using Journal.Core.Documents;

namespace Journal.Core.Tests;

/// <summary>ISS-020: each kind of block answers for itself.</summary>
public class DocBlockTests
{
    private static readonly BlobRef Photo = new(Guid.NewGuid(), "a");
    private static readonly BlobRef Sheet = new(Guid.NewGuid(), "b");

    [Fact(DisplayName = "TC-ENT-07 Each kind of block reports whether it is empty and which vault files it uses")]
    public void BlocksAnswerForThemselves()
    {
        var image = new ImageBlock { Image = Photo };
        var file = new AttachmentBlock { File = Sheet };

        Assert.True(new ParagraphBlock().IsEmpty());
        Assert.True(ParagraphBlock.OfText("  \n ").IsEmpty());
        Assert.False(ParagraphBlock.OfText("words").IsEmpty());
        Assert.False(image.IsEmpty());
        Assert.False(file.IsEmpty());

        Assert.Empty(ParagraphBlock.OfText("words").ReferencedBlobs());
        Assert.Equal([Photo], image.ReferencedBlobs());
        Assert.Equal([Sheet], file.ReferencedBlobs());
    }

    [Fact(DisplayName = "TC-ENT-07 A document asks its blocks: empty only if every block is; files in block order")]
    public void DocumentAsksItsBlocks()
    {
        Assert.True(new JournalDocument().IsEmpty());
        Assert.True(new JournalDocument([ParagraphBlock.OfText(" "), new ParagraphBlock()]).IsEmpty());
        Assert.False(new JournalDocument([ParagraphBlock.OfText(" "), new ImageBlock { Image = Photo }]).IsEmpty());
        var doc = new JournalDocument([new AttachmentBlock { File = Sheet }, ParagraphBlock.OfText("x"), new ImageBlock { Image = Photo }]);
        Assert.Equal([Sheet, Photo], doc.ReferencedBlobs());
    }

    private sealed class Recorder : IDocBlockVisitor
    {
        public List<string> Seen { get; } = [];
        public void Visit(ParagraphBlock paragraph) => Seen.Add("p");
        public void Visit(ImageBlock image) => Seen.Add("img");
        public void Visit(AttachmentBlock attachment) => Seen.Add("file");
    }

    [Fact(DisplayName = "TC-EDT-13 The visitor reaches each kind of block")]
    public void Visitor()
    {
        var v = new Recorder();
        foreach (var b in new DocBlock[] { new ParagraphBlock(), new ImageBlock { Image = Photo }, new AttachmentBlock { File = Sheet } }) b.Accept(v);
        Assert.Equal(["p", "img", "file"], v.Seen);
    }

    [Fact(DisplayName = "TC-ENT-10 Documents compare by content, not by their blocks' ids")]
    public void SameContentIgnoresIds()
    {
        JournalDocument Make(bool ticked = false, string caption = "c") => new([
            ParagraphBlock.OfText("words"),
            new ParagraphBlock(new TextRun("task")) { List = ListKind.Checklist, IsChecked = ticked },
            new ImageBlock { Image = Photo, Caption = caption },
        ]);

        var a = Make();
        var b = Make();
        Assert.NotEqual(a.Blocks[0].Id, b.Blocks[0].Id);
        Assert.True(Persistence.JournalJson.SameContent(a, b));
        Assert.False(Persistence.JournalJson.SameContent(a, Make(ticked: true)));
        Assert.False(Persistence.JournalJson.SameContent(a, Make(caption: "other")));
        Assert.False(Persistence.JournalJson.SameContent(a, new JournalDocument(a.Blocks.Take(2))));
        // Saved files still keep the ids.
        Assert.Contains(a.Blocks[0].Id.ToString(), Persistence.JournalJson.SerializeDocument(a));
    }
}
