using Journal.Core.Documents;
using Journal.Core.Domain;
using Journal.Core.Persistence;
using Journal.Core.Tests.Support;

namespace Journal.Core.Tests;

public class TagTests
{
    [Fact(DisplayName = "TC-TAG-03 Tag rules: trim, case-insensitive, 1-40 chars, no comma or colon")]
    public void TagRules()
    {
        var a = Tag.Parse("  Family ");
        var b = Tag.Parse("family");
        Assert.Equal(a, b);
        Assert.Equal("Family", a.Name);
        Assert.False(Tag.TryParse("", out _, out _));
        Assert.False(Tag.TryParse(new string('x', 41), out _, out _));
        Assert.False(Tag.TryParse("a,b", out _, out var commaError));
        Assert.Contains("comma", commaError);
        Assert.False(Tag.TryParse("a:b", out _, out _));
        Assert.True(Tag.TryParse(new string('x', 40), out _, out _));
    }
}

public class JournalEntryTests
{
    private static JournalEntry NewEntry() => JournalEntry.Create(TestData.Today, DateTimeOffset.UnixEpoch);

    [Fact(DisplayName = "TC-TAB-01 New entry has one untagged Journal tab")]
    public void NewEntryHasJournalTab()
    {
        var e = NewEntry();
        var tab = Assert.Single(e.Tabs);
        Assert.Equal("Journal", tab.Title);
        Assert.Null(tab.Tag);
    }

    [Fact(DisplayName = "TC-TAB-03 Adding a tab for an existing tag returns the existing tab")]
    public void NoDuplicateTabs()
    {
        var e = NewEntry();
        var first = e.AddTab(Tag.Parse("work"));
        var second = e.AddTab(Tag.Parse("WORK"));
        Assert.Same(first, second);
        Assert.Equal(2, e.Tabs.Count);
    }

    [Fact(DisplayName = "TC-TAB-04 Renaming a tab changes its tag; duplicates are rejected")]
    public void RenameTab()
    {
        var e = NewEntry();
        var work = e.AddTab(Tag.Parse("work"));
        e.AddTab(Tag.Parse("family"));
        e.RetagTab(work.Id, Tag.Parse("career"));
        Assert.Equal("career", work.Title);
        Assert.Throws<DuplicateTagException>(() => e.RetagTab(work.Id, Tag.Parse("Family")));
        Assert.Equal("career", work.Title);
    }

    [Fact(DisplayName = "TC-TAB-05 The Journal tab can't be removed; other tabs can")]
    public void RemoveTabs()
    {
        var e = NewEntry();
        var work = e.AddTab(Tag.Parse("work"));
        Assert.Throws<InvalidOperationException>(() => e.RemoveTab(e.DefaultTab.Id));
        e.RemoveTab(work.Id);
        Assert.Single(e.Tabs);
    }

    [Fact(DisplayName = "TC-TAB-06 Moving tabs keeps Journal first")]
    public void MoveTabs()
    {
        var e = NewEntry();
        var a = e.AddTab(Tag.Parse("a"));
        var b = e.AddTab(Tag.Parse("b"));
        e.MoveTab(b.Id, 1);
        Assert.Equal(["Journal", "b", "a"], e.Tabs.Select(t => t.Title));
        e.MoveTab(a.Id, 0);
        Assert.Equal("Journal", e.Tabs[0].Title);
    }

    [Fact(DisplayName = "TC-TAG-01 Tags can be added without creating a tab")]
    public void AddTagWithoutTab()
    {
        var e = NewEntry();
        Assert.True(e.AddTag(Tag.Parse("gratitude")));
        Assert.Contains(Tag.Parse("gratitude"), e.ExtraTags);
        Assert.Single(e.Tabs);
    }

    [Fact(DisplayName = "TC-TAG-02 Entry tags are the union of tab tags and extra tags")]
    public void AllTags()
    {
        var e = NewEntry();
        e.AddTab(Tag.Parse("family"));
        e.AddTab(Tag.Parse("work"));
        e.AddTag(Tag.Parse("gratitude"));
        Assert.Equal(["family", "work", "gratitude"], e.AllTags().Select(t => t.Name));
    }

    [Fact(DisplayName = "TC-TAG-04 Tab tags can't be removed from the tag bar")]
    public void TabTagsNotRemovableAsExtraTags()
    {
        var e = NewEntry();
        e.AddTab(Tag.Parse("work"));
        Assert.False(e.AddTag(Tag.Parse("work")));
        Assert.False(e.RemoveTag(Tag.Parse("work")));
        Assert.True(e.IsTabTag(Tag.Parse("work")));
    }

    [Fact(DisplayName = "FR-ENT-07 Entry with only whitespace is empty; tags make it non-empty")]
    public void EmptyEntry()
    {
        var e = NewEntry();
        e.DefaultTab.Document.Blocks.Add(ParagraphBlock.OfText("   "));
        Assert.True(e.IsEmpty());
        e.AddTag(Tag.Parse("x"));
        Assert.False(e.IsEmpty());
    }

    [Fact(DisplayName = "Entry serialization round trip keeps tabs, tags and inspiration")]
    public void SerializationRoundTrip()
    {
        var e = TestData.Entry(TestData.Today, "Hello", "family");
        e.AddTag(Tag.Parse("gratitude"));
        e.Inspiration = new DailyInspiration(new Verse("Psalm 23:1", "Yahweh is my shepherd", "WEB"), new Quote("q", "Seneca", "Morals", "Stoic"));
        e.IsInspirationPinned = true;
        var back = JournalJson.DeserializeEntry(JournalJson.SerializeEntry(e));
        Assert.Equal(e.Date, back.Date);
        Assert.Equal(["Journal", "family"], back.Tabs.Select(t => t.Title));
        Assert.Equal("Hello", back.DefaultTab.Document.ExtractPlainText());
        Assert.Equal("Psalms", back.Inspiration.Verse!.Book);
        Assert.True(back.IsInspirationPinned);
        Assert.Contains(Tag.Parse("gratitude"), back.ExtraTags);
    }

    [Fact(DisplayName = "TC-NFR-21 Entry from a newer format version is reported, not misread")]
    public void NewerFormatVersion()
    {
        var json = System.Text.Encoding.UTF8.GetBytes("{\"formatVersion\":99,\"date\":\"2026-10-15\",\"tabs\":[]}");
        Assert.Throws<UnsupportedFormatVersionException>(() => JournalJson.DeserializeEntry(json));
    }
}

public class DocumentTests
{
    [Fact(DisplayName = "Plain text extraction joins paragraphs")]
    public void ExtractText()
    {
        var doc = new JournalDocument([
            new ParagraphBlock(new TextRun("Hello "), new TextRun("world", RunFormat.Bold)),
            new ImageBlock { Image = new BlobRef(Guid.NewGuid(), "x"), Caption = "cap" },
            ParagraphBlock.OfText("Second"),
        ]);
        Assert.Equal("Hello world\nSecond", doc.ExtractPlainText());
        Assert.False(doc.IsEmpty());
        Assert.Single(doc.ReferencedBlobs());
    }

    [Fact(DisplayName = "Checklist counts ignore empty items")]
    public void ChecklistCounts()
    {
        var doc = new JournalDocument([
            new ParagraphBlock(new TextRun("Buy seeds")) { List = ListKind.Checklist },
            new ParagraphBlock(new TextRun("Call mum")) { List = ListKind.Checklist, IsChecked = true },
            new ParagraphBlock { List = ListKind.Checklist },
        ]);
        Assert.Equal((1, 1), doc.ChecklistCounts());
    }

    [Fact(DisplayName = "NormalizeRuns merges equal formatting and drops empty runs")]
    public void NormalizeRuns()
    {
        var p = new ParagraphBlock(new TextRun("a"), new TextRun(""), new TextRun("b"), new TextRun("c", RunFormat.Bold)).NormalizeRuns();
        Assert.Equal(2, p.Runs.Count);
        Assert.Equal("ab", p.Runs[0].Text);
    }

    [Theory(DisplayName = "ArgbColor parses #RRGGBB and #AARRGGBB")]
    [InlineData("#C00000", "#FFC00000")]
    [InlineData("#80FFFF00", "#80FFFF00")]
    public void ColorParse(string input, string expected) => Assert.Equal(expected, ArgbColor.Parse(input).ToHex());

    [Fact(DisplayName = "Document JSON round trip keeps every block type and run format")]
    public void DocumentJsonRoundTrip()
    {
        var doc = new JournalDocument([
            new ParagraphBlock(new TextRun("Title")) { Style = ParagraphStyle.Heading1 },
            new ParagraphBlock(new TextRun("x", RunFormat.Bold | RunFormat.Strikethrough) { FontFamily = "Georgia", FontSize = 14, Foreground = ArgbColor.Parse("#C00000"), Highlight = ArgbColor.Parse("#FFFF00") }),
            new ParagraphBlock(new TextRun("item")) { List = ListKind.Numbered, ListLevel = 2 },
            new ParagraphBlock(new TextRun("todo")) { List = ListKind.Checklist, IsChecked = true },
            new ImageBlock { Image = new BlobRef(Guid.NewGuid(), "abc"), Caption = "Garden", DisplayWidth = 240, PixelWidth = 10, PixelHeight = 5 },
            new AttachmentBlock { File = new BlobRef(Guid.NewGuid(), "def"), FileName = "a.xlsx", SizeBytes = 24576, Description = "tomato yields" },
        ]);
        var json = JournalJson.SerializeDocument(doc);
        Assert.Equal(json, JournalJson.SerializeDocument(JournalJson.DeserializeDocument(json)));
        Assert.Equal("24 KB", AttachmentBlock.FormatSize(24576));
    }
}
