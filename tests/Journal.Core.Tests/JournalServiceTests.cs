using Journal.Core.Documents;
using Journal.Core.Domain;
using Journal.Core.Inspiration;
using Journal.Core.Services;
using Journal.Core.Tests.Support;

namespace Journal.Core.Tests;

public class JournalServiceTests
{
    [Fact(DisplayName = "TC-ENT-01 One entry per date; the second save replaces the first")]
    public async Task OneEntryPerDate()
    {
        var (svc, repo, _, _) = TestData.Service();
        await svc.InitializeAsync();
        await svc.SaveAsync(TestData.Entry(TestData.Today, "first"));
        await svc.SaveAsync(TestData.Entry(TestData.Today, "second"));
        Assert.Single(repo.Stored);
        var loaded = (await svc.OpenDayAsync(TestData.Today)).Entry!;
        Assert.Equal("second", loaded.DefaultTab.Document.ExtractPlainText());
    }

    [Fact(DisplayName = "TC-ENT-02 Past, present and future dates are all editable")]
    public async Task AnyDate()
    {
        var (svc, _, _, _) = TestData.Service();
        await svc.InitializeAsync();
        foreach (var d in new[] { new DateOnly(2020, 2, 29), TestData.Today, new DateOnly(2031, 12, 31) })
        {
            var e = (await svc.OpenDayAsync(d)).Entry!;
            e.DefaultTab.Document.Blocks.Add(ParagraphBlock.OfText("x"));
            Assert.Equal(SaveOutcome.Saved, await svc.SaveAsync(e));
        }
        Assert.Equal(3, svc.Catalog.Count);
    }

    [Fact(DisplayName = "TC-ENT-07 Emptied entries are deleted; never-filled entries are not stored")]
    public async Task EmptyEntries()
    {
        var (svc, repo, _, _) = TestData.Service();
        await svc.InitializeAsync();
        var e = (await svc.OpenDayAsync(TestData.Today)).Entry!;
        Assert.Equal(SaveOutcome.Skipped, await svc.SaveAsync(e));
        Assert.Empty(repo.Stored);
        e.DefaultTab.Document.Blocks.Add(ParagraphBlock.OfText("text"));
        await svc.SaveAsync(e);
        e.DefaultTab.Document.Blocks.Clear();
        Assert.Equal(SaveOutcome.Deleted, await svc.SaveAsync(e));
        Assert.Empty(repo.Stored);
        Assert.False(svc.HasEntry(TestData.Today));
    }

    [Fact(DisplayName = "TC-ENT-08 Deleting an entry removes its images and files")]
    public async Task DeleteEntryRemovesBlobs()
    {
        var (svc, repo, blobs, _) = TestData.Service();
        await svc.InitializeAsync();
        var e = (await svc.OpenDayAsync(TestData.Today)).Entry!;
        var img = await svc.AddBlobAsync(new MemoryStream([1, 2, 3]));
        var file = await svc.AddBlobAsync(new MemoryStream([4, 5]));
        e.DefaultTab.Document.Blocks.Add(new ImageBlock { Image = img });
        e.DefaultTab.Document.Blocks.Add(new AttachmentBlock { File = file, FileName = "a.txt" });
        await svc.SaveAsync(e);
        await svc.DeleteEntryAsync(TestData.Today);
        Assert.Empty(repo.Stored);
        Assert.Empty(blobs.Blobs);
    }

    [Fact(DisplayName = "TC-ENT-09 An unreadable entry is reported and never overwritten")]
    public async Task UnreadableEntry()
    {
        var (svc, repo, _, _) = TestData.Service();
        await svc.InitializeAsync();
        repo.Corrupt.Add(TestData.Today);
        var load = await svc.OpenDayAsync(TestData.Today);
        Assert.True(load.IsUnreadable);
        Assert.Contains("could not be read", load.Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync(TestData.Entry(TestData.Today, "overwrite")));
        Assert.False((await svc.OpenDayAsync(TestData.Today.AddDays(1))).IsUnreadable);
    }

    [Fact(DisplayName = "TC-INSP-09/10 Saved entries keep their verse and quote after settings change")]
    public async Task PinnedInspiration()
    {
        var settings = new InspirationSettings();
        var (svc, _, _, _) = TestData.Service(settings: () => settings);
        await svc.InitializeAsync();
        var e = (await svc.OpenDayAsync(TestData.Today)).Entry!;
        var original = e.Inspiration;
        e.DefaultTab.Document.Blocks.Add(ParagraphBlock.OfText("x"));
        await svc.SaveAsync(e);
        settings = new InspirationSettings { Mode = SelectionMode.RandomPerDay, Traditions = new HashSet<string> { "Greek" } };
        Assert.Equal(original, (await svc.OpenDayAsync(TestData.Today)).Entry!.Inspiration);
        var fresh = (await svc.OpenDayAsync(TestData.Today.AddDays(1))).Entry!;
        Assert.Equal("Greek", fresh.Inspiration.Quote!.Tradition);
    }

    [Fact(DisplayName = "TC-INSP-13 A saved partial verse is shown as the full passage")]
    public async Task PartialVerseUpgraded()
    {
        var full = new Verse("1 Corinthians 13:4-7", "Love is patient and is kind. … and endures all things.", "WEB");
        var inspiration = new DailyInspirationService(
            [new ListVerseSource(SourceNames.BuiltIn, () => [full])], [new ListQuoteSource(SourceNames.BuiltIn, () => TestData.Quotes())]);
        var repo = new InMemoryEntryRepository();
        var svc = new JournalService(repo, new InMemoryBlobStore(), new InMemoryCatalogStore(), new Search.InMemorySearchIndex(),
            inspiration, new FakeClock(), () => new(), Guid.Empty);
        await svc.InitializeAsync();
        var e = TestData.Entry(new DateOnly(2026, 10, 13), "saved earlier");
        e.Inspiration = new DailyInspiration(new Verse("1 Corinthians 13:7", "bears all things, believes all things, hopes all things, and endures all things.", "WEB"), null);
        await svc.SaveAsync(e);
        Assert.Equal(full, (await svc.OpenDayAsync(e.Date)).Entry!.Inspiration.Verse);
        var whole = new Verse("Psalm 23:1", "Yahweh is my shepherd; I shall lack nothing.", "WEB");
        Assert.Same(whole, inspiration.CompletePassage(whole));
    }

    [Fact(DisplayName = "TC-NFR-11 Missing or damaged catalog is rebuilt from entries")]
    public async Task CatalogRebuild()
    {
        var (svc, repo, blobs, catalog) = TestData.Service();
        await svc.InitializeAsync();
        await svc.SaveAsync(TestData.Entry(TestData.Today, "garden"));
        catalog.Saved = null;
        var svc2 = new JournalService(repo, blobs, catalog, new Search.InMemorySearchIndex(), TestData.Inspiration(), new FakeClock(), () => new(), Guid.Empty);
        var r = await svc2.InitializeAsync();
        Assert.True(r.CatalogRebuilt);
        Assert.Single(svc2.Search.Search("garden").Hits);
        catalog.Corrupt = true;
        var svc3 = new JournalService(repo, blobs, catalog, new Search.InMemorySearchIndex(), TestData.Inspiration(), new FakeClock(), () => new(), Guid.Empty);
        Assert.True((await svc3.InitializeAsync()).CatalogRebuilt);
        Assert.True(svc3.HasEntry(TestData.Today));
    }

    [Fact(DisplayName = "TC-NFR-12 Unreferenced blobs are collected; referenced and protected ones are kept")]
    public async Task GarbageCollection()
    {
        var (svc, _, blobs, _) = TestData.Service();
        await svc.InitializeAsync();
        var shared = await svc.AddBlobAsync(new MemoryStream([1]));
        var orphan = await svc.AddBlobAsync(new MemoryStream([2]));
        var undoable = await svc.AddBlobAsync(new MemoryStream([3]));
        var a = TestData.Entry(TestData.Today, "a");
        a.DefaultTab.Document.Blocks.Add(new ImageBlock { Image = shared });
        var b = TestData.Entry(TestData.Today.AddDays(1), "b");
        b.DefaultTab.Document.Blocks.Add(new ImageBlock { Image = shared });
        await svc.SaveAsync(a);
        await svc.SaveAsync(b);
        // Removing the image from one entry keeps the blob used by the other.
        a.DefaultTab.Document.Blocks.RemoveAll(x => x is ImageBlock);
        await svc.SaveAsync(a);
        Assert.Equal(1, await svc.CollectGarbageAsync([undoable.Id]));
        Assert.True(blobs.Exists(shared.Id));
        Assert.True(blobs.Exists(undoable.Id));
        Assert.False(blobs.Exists(orphan.Id));
    }

    [Fact(DisplayName = "FR-CAL-09 EntryChanged fires on save and on removal")]
    public async Task EntryChangedEvents()
    {
        var (svc, _, _, _) = TestData.Service();
        await svc.InitializeAsync();
        var events = new List<(DateOnly, bool)>();
        svc.EntryChanged += (_, e) => events.Add((e.Date, e.Exists));
        var e = TestData.Entry(TestData.Today, "x");
        await svc.SaveAsync(e);
        e.DefaultTab.Document.Blocks.Clear();
        await svc.SaveAsync(e);
        Assert.Equal([(TestData.Today, true), (TestData.Today, false)], events);
    }

    [Fact(DisplayName = "FR-CAL-05 Days with open checklist items are reported")]
    public async Task OpenItems()
    {
        var (svc, _, _, _) = TestData.Service();
        await svc.InitializeAsync();
        var e = TestData.Entry(TestData.Today.AddDays(5));
        e.DefaultTab.Document.Blocks.Add(new ParagraphBlock(new TextRun("Doctor")) { List = ListKind.Checklist });
        await svc.SaveAsync(e);
        Assert.Equal([TestData.Today.AddDays(5)], svc.OpenItemDates(TestData.Today, TestData.Today.AddDays(30)));
    }
}
