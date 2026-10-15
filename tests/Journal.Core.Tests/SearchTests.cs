using System.Diagnostics;
using Journal.Core.Documents;
using Journal.Core.Domain;
using Journal.Core.Persistence;
using Journal.Core.Search;
using Journal.Core.Tests.Support;

namespace Journal.Core.Tests;

public class SearchTests
{
    private static readonly DateOnly Oct13 = new(2026, 10, 13), Sep28 = new(2026, 9, 28), Aug02 = new(2026, 8, 2), Nov03 = new(2026, 11, 3);
    private readonly SearchService _search;
    private readonly InMemorySearchIndex _index = new();

    public SearchTests()
    {
        var oct13 = JournalEntry.Create(Oct13, DateTimeOffset.UnixEpoch);
        var family = oct13.AddTab(Tag.Parse("family"));
        family.Document.Blocks.Add(ParagraphBlock.OfText("Planted the garden with the kids"));
        family.Document.Blocks.Add(new ImageBlock { Image = new BlobRef(Guid.NewGuid(), ""), Caption = "Garden at sunrise" });

        var sep28 = JournalEntry.Create(Sep28, DateTimeOffset.UnixEpoch);
        sep28.DefaultTab.Document.Blocks.Add(ParagraphBlock.OfText("Prayer meeting at church"));
        sep28.DefaultTab.Document.Blocks.Add(new AttachmentBlock { File = new BlobRef(Guid.NewGuid(), ""), FileName = "harvest-weights.xlsx", Description = "tomato yields" });
        sep28.DefaultTab.Document.Blocks.Add(new ParagraphBlock(new TextRun("Buy seeds")) { List = ListKind.Checklist });

        var aug02 = JournalEntry.Create(Aug02, DateTimeOffset.UnixEpoch);
        aug02.AddTag(Tag.Parse("family"));
        aug02.DefaultTab.Document.Blocks.Add(ParagraphBlock.OfText("Café visit after the walk"));

        var nov03 = JournalEntry.Create(Nov03, DateTimeOffset.UnixEpoch);
        nov03.DefaultTab.Document.Blocks.Add(new ParagraphBlock(new TextRun("Doctor appointment")) { List = ListKind.Checklist, IsChecked = true });

        _index.Rebuild(new Catalog(new[] { oct13, sep28, aug02, nov03 }.Select(CatalogEntry.From)));
        _search = new SearchService(new SearchQueryParser(new FakeClock()), _index);
    }

    private DateOnly[] Dates(string q) => _search.Search(q).Hits.Select(h => h.Date).Distinct().ToArray();

    [Fact(DisplayName = "TC-SRCH-02 Words match text, captions, file names, descriptions and tags")]
    public void MatchesAllSources()
    {
        Assert.Equal([Oct13], Dates("garden"));
        var sunrise = Assert.Single(_search.Search("sunrise").Hits);
        Assert.Equal(MatchSource.ImageCaption, sunrise.Source);
        Assert.Equal(MatchSource.Attachment, Assert.Single(_search.Search("harvest").Hits).Source);
        Assert.Equal([Sep28], Dates("tomato"));
        Assert.Equal([Oct13, Aug02], Dates("family"));
    }

    [Fact(DisplayName = "TC-SRCH-03 Case- and accent-insensitive word-prefix matching")]
    public void CaseAccentPrefix()
    {
        Assert.Equal([Oct13], Dates("GARDEN"));
        Assert.Equal([Oct13], Dates("gard"));
        Assert.Equal([Aug02], Dates("cafe"));
        Assert.Empty(Dates("arden"));
    }

    [Fact(DisplayName = "TC-SRCH-04 All words must match in the same tab")]
    public void AndSemantics()
    {
        Assert.Equal([Oct13], Dates("garden kids"));
        Assert.Empty(Dates("garden church"));
    }

    [Fact(DisplayName = "TC-SRCH-05 Quoted phrases match exactly")]
    public void Phrases()
    {
        Assert.Equal([Sep28], Dates("\"prayer meeting\""));
        Assert.Empty(Dates("\"meeting prayer\""));
    }

    [Fact(DisplayName = "TC-SRCH-06 tag: filters combine with AND")]
    public void TagFilter()
    {
        Assert.Equal([Oct13, Aug02], Dates("tag:family"));
        Assert.Equal([Oct13], Dates("tag:FAMILY garden"));
        Assert.Empty(Dates("tag:family tag:work"));
    }

    [Fact(DisplayName = "TC-SRCH-07 in: limits where words match")]
    public void Scope()
    {
        var hit = Assert.Single(_search.Search("in:captions garden").Hits);
        Assert.Equal(MatchSource.ImageCaption, hit.Source);
        Assert.Empty(Dates("in:text sunrise"));
        Assert.Equal([Sep28], Dates("in:files tomato"));
    }

    [Fact(DisplayName = "TC-SRCH-08 from:/to: date range, inclusive, with today")]
    public void DateRange()
    {
        Assert.Equal([Sep28], Dates("from:2026-09-01 to:2026-09-30 prayer"));
        Assert.Equal([Nov03], Dates("from:today"));
        Assert.Equal([Aug02], Dates("to:2026-08-02"));
    }

    [Fact(DisplayName = "TC-SRCH-09 is:open and is:done")]
    public void ChecklistFilter()
    {
        Assert.Equal([Sep28], Dates("is:open"));
        Assert.Equal([Nov03], Dates("is:done"));
    }

    [Fact(DisplayName = "TC-SRCH-10 Results newest first with snippet highlights")]
    public void OrderingAndSnippet()
    {
        Assert.Equal([Oct13, Aug02], Dates("family"));
        var longText = string.Join(" ", Enumerable.Repeat("filler words here", 60)) + " the special garden word " + string.Join(" ", Enumerable.Repeat("more filler", 60));
        var (snippet, highlights) = SnippetBuilder.Build(longText, ["garden"], []);
        Assert.True(snippet.Length <= SnippetBuilder.MaxLength + 1, $"length {snippet.Length}");
        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        var h = Assert.Single(highlights);
        Assert.Equal("garden", snippet.Substring(h.Start, h.Length));
    }

    [Fact(DisplayName = "TC-SRCH-12 Unknown operators are plain words; bad dates warn")]
    public void UnknownOperatorsAndBadDates()
    {
        var parser = new SearchQueryParser(new FakeClock());
        var q = parser.Parse("foo:bar garden");
        Assert.Equal(["foo", "bar", "garden"], q.Terms);
        var bad = parser.Parse("from:2026-13-45 garden");
        Assert.Null(bad.From);
        Assert.Single(bad.Warnings);
        Assert.Equal(["garden"], bad.Terms);
    }

    [Fact(DisplayName = "TC-SRCH-15 Index updates incrementally on save and delete")]
    public void IncrementalUpdate()
    {
        var e = TestData.Entry(new DateOnly(2026, 10, 1));
        e.DefaultTab.Document.Blocks.Add(new ImageBlock { Image = new BlobRef(Guid.NewGuid(), ""), Caption = "Lighthouse" });
        _index.Upsert(CatalogEntry.From(e));
        Assert.Single(Dates("lighthouse"));
        _index.Remove(e.Date);
        Assert.Empty(Dates("lighthouse"));
    }

    [Fact(DisplayName = "TC-NFR-15 Search over 3,650 entries is fast", Skip = null)]
    public void Performance()
    {
        var rnd = new Random(1);
        var words = "garden prayer family work church walk river mountain coffee letter friend book music rain sunrise harvest".Split(' ');
        var catalog = new Catalog(Enumerable.Range(0, 3650).Select(i =>
        {
            var e = TestData.Entry(new DateOnly(2016, 1, 1).AddDays(i),
                string.Join(' ', Enumerable.Range(0, 120).Select(_ => words[rnd.Next(words.Length)])), i % 3 == 0 ? "family" : "work");
            return CatalogEntry.From(e);
        }));
        var index = new InMemorySearchIndex();
        index.Rebuild(catalog);
        var svc = new SearchService(new SearchQueryParser(new FakeClock()), index);
        string[] queries = ["garden", "tag:family river", "\"coffee letter\"", "gard pray", "in:text music", "from:2020-01-01 to:2020-12-31 rain",
            "harvest", "friend book", "is:open", "zzz", "sun", "tag:work", "mountain walk", "church", "letter", "music rain", "coffee", "river", "book", "walk"];
        svc.Search("warmup");
        var times = new List<double>();
        foreach (var q in queries)
        {
            var sw = Stopwatch.StartNew();
            svc.Search(q);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        Assert.All(times, t => Assert.True(t <= 200, $"query took {t} ms"));
        Assert.True(times.Order().ElementAt(times.Count / 2) <= 50, "median above 50 ms");
    }
}
