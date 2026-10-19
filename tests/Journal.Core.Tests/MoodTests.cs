using System.Text.Json;
using Journal.Core.Domain;
using Journal.Core.Persistence;
using Journal.Core.Services;
using Journal.Core.Tests.Support;

namespace Journal.Core.Tests;

/// <summary>ISS-013: a sad, neutral or happy mood per day, kept with the entry and in the catalog.</summary>
public class MoodTests
{
    [Fact(DisplayName = "TC-MOOD-01 Set, change and clear a day's mood; a mood alone keeps the day; future days refuse a mood")]
    public async Task SetChangeClear()
    {
        var (svc, repo, _, _) = TestData.Service();
        await svc.InitializeAsync();
        var today = TestData.Today;

        var entry = (await svc.OpenDayAsync(today)).Entry!;
        entry.SetMood(Mood.Happy, today);
        Assert.Equal(SaveOutcome.Saved, await svc.SaveAsync(entry));   // no text, but the mood is kept
        Assert.Equal(Mood.Happy, (await svc.OpenDayAsync(today)).Entry!.Mood);
        Assert.True(svc.HasEntry(today));

        entry.SetMood(Mood.Sad, today);
        await svc.SaveAsync(entry);
        Assert.Equal(Mood.Sad, (await svc.OpenDayAsync(today)).Entry!.Mood);

        entry.SetMood(Mood.None, today);
        Assert.Equal(SaveOutcome.Deleted, await svc.SaveAsync(entry));   // nothing left: the day is removed
        Assert.Empty(repo.Stored);

        var tomorrow = (await svc.OpenDayAsync(today.AddDays(1))).Entry!;
        Assert.Throws<InvalidOperationException>(() => tomorrow.SetMood(Mood.Happy, today));
        tomorrow.SetMood(Mood.None, today);   // clearing is always allowed
    }

    [Fact(DisplayName = "TC-MOOD-02 The mood is stored in the entry and the catalog; older files without one read as no mood")]
    public async Task Storage()
    {
        var (svc, repo, _, _) = TestData.Service();
        await svc.InitializeAsync();
        var entry = TestData.Entry(TestData.Today, "text");
        entry.SetMood(Mood.Neutral, TestData.Today);
        await svc.SaveAsync(entry);
        Assert.Contains("\"mood\":\"neutral\"", System.Text.Encoding.UTF8.GetString(repo.Stored[TestData.Today]));
        Assert.Equal(Mood.Neutral, svc.Catalog.Get(TestData.Today)!.Mood);

        // A day without a mood writes no mood at all, so files look as they did before ISS-013.
        await svc.SaveAsync(TestData.Entry(TestData.Today.AddDays(-1), "plain"));
        Assert.DoesNotContain("mood", System.Text.Encoding.UTF8.GetString(repo.Stored[TestData.Today.AddDays(-1)]));

        // A catalog written before ISS-013 has no mood field.
        var oldCatalog = """{"date":"2026-10-01","tags":[],"tabs":[],"openChecklistItems":0,"doneChecklistItems":0,"blobIds":[]}""";
        Assert.Equal(Mood.None, JsonSerializer.Deserialize<CatalogEntry>(oldCatalog, JournalJson.Options)!.Mood);
        var roundTrip = JsonSerializer.Deserialize<CatalogEntry>(JsonSerializer.Serialize(svc.Catalog.Get(TestData.Today), JournalJson.Options), JournalJson.Options)!;
        Assert.Equal(Mood.Neutral, roundTrip.Mood);
    }

    [Fact(DisplayName = "TC-MOOD-04 Counts cover the last 30 days and the last 12 months, both ending today")]
    public async Task Counts()
    {
        var clock = new FakeClock();   // today is 2026-10-15
        var (svc, _, _, _) = TestData.Service(clock);
        await svc.InitializeAsync();
        var today = clock.Today;
        async Task Mark(DateOnly d, Mood m)
        {
            var e = (await svc.OpenDayAsync(d)).Entry!;
            e.SetMood(m, today);
            await svc.SaveAsync(e);
        }
        await Mark(today, Mood.Happy);                         // both periods
        await Mark(today.AddDays(-29), Mood.Sad);              // first day of the 30
        await Mark(today.AddDays(-30), Mood.Neutral);          // just outside the 30, inside the 12 months
        await Mark(new DateOnly(2025, 10, 16), Mood.Happy);    // first day of the 12 months
        await Mark(new DateOnly(2025, 10, 15), Mood.Sad);      // just outside the 12 months
        await svc.SaveAsync(TestData.Entry(today.AddDays(-2), "no mood"));   // not counted

        var s = svc.GetMoodSummary();
        Assert.Equal(new MoodCounts(1, 0, 1), s.Last30Days);
        Assert.Equal(new MoodCounts(1, 1, 2), s.Last12Months);
        Assert.Equal((today.AddDays(-29), new DateOnly(2025, 10, 16)), MoodSummary.PeriodStarts(today));

        // Moods for the calendar: only days that have one.
        var moods = svc.Moods(today.AddDays(-40), today);
        Assert.Equal(3, moods.Count);
        Assert.Equal(Mood.Sad, moods[today.AddDays(-29)]);
    }
}
