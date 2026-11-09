using System.Text.Json;
using Journal.Core.Domain;
using Journal.Core.Inspiration;
using Journal.Core.Persistence;
using Journal.Core.Settings;
using Journal.Core.Tests.Support;

namespace Journal.Core.Tests;

/// <summary>ISS-012: verses from several traditions, chosen evenly, with the scripture as the card label.</summary>
public class ScriptureTests
{
    private static readonly Guid Vault = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static Verse Passage(string tradition, string scripture, string book, int i) =>
        new($"{book} {i}", $"{scripture} passage {i}.", scripture[..3]) { Tradition = tradition, Scripture = scripture, Book = book };

    /// <summary>The test Bible (400 verses) plus 50 Quran passages and 30 + 30 Taoist ones.</summary>
    private static IReadOnlyList<Verse> Mixed() =>
    [
        .. TestData.Verses().Select(v => v with { Tradition = VerseTraditions.Christianity, Scripture = VerseTraditions.Bible }),
        .. Enumerable.Range(1, 50).Select(i => Passage("Islam", "Quran", "Quran", i)),
        .. Enumerable.Range(1, 30).Select(i => Passage("Taoism", "Tao Te Ching", "Tao Te Ching", i)),
        .. Enumerable.Range(1, 30).Select(i => Passage("Taoism", "Chuang Tzu", "Chuang Tzu", i)),
    ];

    private static DailyInspirationService Service(IReadOnlyList<Verse> verses) =>
        new([new ListVerseSource(SourceNames.BuiltIn, () => verses)], [new ListQuoteSource(SourceNames.BuiltIn, () => TestData.Quotes())]);

    private static InspirationSettings Ticked(params string[] traditions) =>
        new() { ShowQuote = false, VerseTraditions = traditions.ToHashSet(StringComparer.OrdinalIgnoreCase) };

    [Fact(DisplayName = "TC-INSP-19 Only Christianity by default, and then each day's verse is exactly what it was before")]
    public void DefaultUnchanged()
    {
        var bibleOnly = TestData.Inspiration();
        var mixed = Service(Mixed());
        foreach (var mode in new[] { SelectionMode.DayOfYear, SelectionMode.RandomPerDay })
            for (int d = 0; d < 400; d++)
            {
                var day = TestData.Today.AddDays(d);
                var s = new InspirationSettings { Mode = mode };
                var before = bibleOnly.For(day, s, Vault).Inspiration.Verse!;
                var after = mixed.For(day, s, Vault).Inspiration.Verse!;
                Assert.Equal(before.Reference, after.Reference);
                Assert.Equal(VerseTraditions.Christianity, after.Tradition);
            }
        Assert.Equal([VerseTraditions.Christianity], new InspirationSettings().VerseTraditions);
    }

    [Fact(DisplayName = "TC-INSP-19 With several traditions ticked, the days share them evenly, by day of year and at random")]
    public void Even()
    {
        var svc = Service(Mixed());
        var s = Ticked("Christianity", "Islam", "Taoism");

        // By day of year: the traditions take turns, and each one's passages come round in order.
        var byDay = Enumerable.Range(0, 300).Select(d => svc.For(new DateOnly(2026, 1, 1).AddDays(d), s, Vault).Inspiration.Verse!).ToList();
        Assert.Equal(["Christianity", "Islam", "Taoism", "Christianity", "Islam", "Taoism"], byDay.Take(6).Select(v => v.Tradition));
        Assert.Equal(50, byDay.Where(v => v.Tradition == "Islam").Select(v => v.Reference).Distinct().Count());
        Assert.Equal(60, byDay.Where(v => v.Tradition == "Taoism").Select(v => v.Reference).Distinct().Count());
        var nextYear = svc.For(new DateOnly(2027, 1, 1).AddDays(100), s, Vault).Inspiration.Verse!;
        Assert.Equal(byDay[100], nextYear);   // the same date gets the same passage every year

        // At random: about a third each, although the Bible has four times as many verses as the rest together.
        var random = s with { Mode = SelectionMode.RandomPerDay };
        var counts = Enumerable.Range(0, 900).Select(d => svc.For(TestData.Today.AddDays(d), random, Vault).Inspiration.Verse!.Tradition!)
            .GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(3, counts.Count);
        Assert.All(counts.Values, n => Assert.InRange(n, 230, 370));
        Assert.Equal(svc.For(TestData.Today, random, Vault).Inspiration.Verse, svc.For(TestData.Today, random, Vault).Inspiration.Verse);

        // Shuffle reaches every ticked tradition, and never returns the current passage.
        var current = svc.For(TestData.Today, s, Vault).Inspiration;
        var seen = new HashSet<string>();
        var rng = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            var next = svc.Shuffle(current, s, rng).Verse!;
            Assert.NotEqual(current.Verse, next);
            seen.Add(next.Tradition!);
        }
        Assert.Equal(3, seen.Count);
    }

    [Fact(DisplayName = "TC-INSP-19 Book choices apply per tradition; a plain book name, as saved before, is a Bible book")]
    public void Books()
    {
        var svc = Service(Mixed());
        var s = Ticked("Christianity", "Taoism") with { VerseBooks = new HashSet<string> { "Psalms", "Taoism/Chuang Tzu" } };
        for (int d = 0; d < 120; d++)
        {
            var v = svc.For(TestData.Today.AddDays(d), s with { Mode = SelectionMode.RandomPerDay }, Vault).Inspiration.Verse!;
            Assert.Equal(v.Tradition == "Taoism" ? "Chuang Tzu" : "Psalms", v.Book);
        }
        // A tradition with no ticked book keeps all its books.
        var islam = Ticked("Islam", "Christianity") with { VerseBooks = new HashSet<string> { "Psalms" } };
        var books = Enumerable.Range(0, 60).Select(d => svc.For(TestData.Today.AddDays(d), islam, Vault).Inspiration.Verse!.Book).ToHashSet();
        Assert.Equal(["Psalms", "Quran"], books.Order());
    }

    [Fact(DisplayName = "TC-INSP-19 No tradition ticked shows all verses, with a warning; imported verses ignore the traditions")]
    public void NoneTickedAndImported()
    {
        var svc = Service(Mixed());
        var none = svc.For(TestData.Today, Ticked(), Vault);
        Assert.NotNull(none.Inspiration.Verse);
        Assert.Contains("No verse traditions are ticked", Assert.Single(none.Warnings));

        var imported = new[] { new Verse("Mine 1:1", "My own verse.", "") };
        var withImports = new DailyInspirationService(
            [new ListVerseSource(SourceNames.BuiltIn, Mixed), new ListVerseSource(SourceNames.Imported, () => imported)], []);
        var r = withImports.For(TestData.Today, Ticked("Islam") with { VerseSource = SourceNames.Imported }, Vault);
        Assert.Equal("Mine 1:1", r.Inspiration.Verse!.Reference);
        Assert.Empty(r.Warnings);
    }

    [Fact(DisplayName = "TC-INSP-19 Traditions listed for Settings: Christianity first, then alphabetically, each with its books")]
    public void Available()
    {
        var t = Service(Mixed()).AvailableVerseTraditions();
        Assert.Equal(["Christianity", "Islam", "Taoism"], t.Select(x => x.Name));
        Assert.Equal(["Tao Te Ching", "Chuang Tzu"], t[2].Scriptures);
        Assert.Equal(["Tao Te Ching", "Chuang Tzu"], t[2].Books);
        Assert.Equal(["Quran"], t[1].Books);
        Assert.Equal(["Proverbs", "Psalms"], t[0].Books.Order());
    }

    [Fact(DisplayName = "TC-INSP-18 The card label names the scripture; days saved before show BIBLE, imported verses VERSE")]
    public void Labels()
    {
        Assert.Equal("QURAN", Passage("Islam", "Quran", "Quran", 1).CardLabel());
        Assert.Equal("TAO TE CHING", Passage("Taoism", "Tao Te Ching", "Tao Te Ching", 1).CardLabel());

        // Stored before ISS-012: no tradition or scripture in the saved day.
        var old = JsonSerializer.Deserialize<Verse>("{\"reference\":\"John 3:16\",\"text\":\"For God so loved the world.\",\"translation\":\"WEB\"}", JournalJson.Options)!;
        Assert.Equal("BIBLE", old.CardLabel());
        Assert.Equal(VerseTraditions.Christianity, old.TraditionName());
        Assert.Equal("VERSE", new Verse("Mine 1:1", "My own verse.", "").CardLabel());
        Assert.Equal("GITA", (new Verse("Mine 1:1", "My own verse.", "") { Scripture = "Gita" }).CardLabel());

        // A new day keeps its tradition and scripture when saved and read back; an old one writes neither.
        var quran = Passage("Islam", "Quran", "Quran", 1);
        var back = JsonSerializer.Deserialize<Verse>(JsonSerializer.Serialize(quran, JournalJson.Options), JournalJson.Options)!;
        Assert.Equal(quran, back);
        Assert.DoesNotContain("scripture", JsonSerializer.Serialize(old, JournalJson.Options));
    }

    [Fact(DisplayName = "TC-INSP-19 Settings from before ISS-012 mean Christianity only; the ticked traditions are saved")]
    public void SettingsFile()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"inspiration\":{\"showVerse\":true,\"verseBooks\":[\"Psalms\"]}}", JournalJson.Options)!;
        Assert.Null(old.Inspiration.VerseTraditions);
        Assert.Equal([VerseTraditions.Christianity], old.Inspiration.ToSettings().VerseTraditions);
        Assert.Equal(["Psalms"], old.Inspiration.ToSettings().VerseBooks);

        var chosen = new InspirationOptions { VerseTraditions = ["Islam", "Taoism"] };
        Assert.Equal(["Islam", "Taoism"], chosen.ToSettings().VerseTraditions.Order());
        Assert.Empty(new InspirationOptions { VerseTraditions = [] }.ToSettings().VerseTraditions);
    }
}
