using System.Globalization;
using Journal.Core.Domain;
using Journal.Core.Tests.Support;
using Journal.Core.Tracking;

namespace Journal.Core.Tests;

/// <summary>ISS-030: values the user tracks, read from the journal and grouped by week or month.</summary>
public class TrackingTests
{
    private static readonly Measure Weight = new("Weight", "lb", MeasurePeriod.Weekly);
    private static readonly Measure Pressure = new("Blood pressure", "mmHg", MeasurePeriod.Monthly);

    private static IReadOnlyList<Reading> ReadText(string text) =>
        ValueReader.FromText(text).Select(r => r.Reading).ToList();

    [Fact(DisplayName = "TC-TRK-01 ##name=value lines give values; invalid ones are ignored")]
    public void ParsesTaggedValues()
    {
        Assert.Equal([new Reading("weight", new(182.4))], ReadText("Morning walk.\n##weight=182.4"));
        Assert.Equal([new Reading("weight", new(182.4))], ReadText("##Weight = 182,4"));
        Assert.Equal([new Reading("blood pressure", new(120, 80))], ReadText("##blood  Pressure=120/80 after coffee"));
        Assert.Equal([new Reading("temperature", new(-3.5))], ReadText("##temperature=-3.5."));
        Assert.Equal([new Reading("a", new(1)), new Reading("b", new(2))], ReadText("##a=1 and ##b=2"));

        foreach (var invalid in new[] { "##weight=heavy", "##weight=", "#weight=180", "##weight 180", "##=180", "##weight=1,250,000", "##weight=12abc", "##weight=1/2/3" })
            Assert.Empty(ReadText(invalid));

        Assert.True(ValueReader.TryParseValue(" 72 ", out var v));
        Assert.Equal(new TrackedValue(72), v);
        Assert.False(ValueReader.TryParseValue("NaN", out _));
        Assert.Equal("120/80", new TrackedValue(120, 80).Format(CultureInfo.InvariantCulture));
        Assert.Equal("182.4", new TrackedValue(182.4).Format(CultureInfo.InvariantCulture));
    }

    [Fact(DisplayName = "TC-TRK-02 A tab named after a measure gives its first number; numbers in ##lines and inside words don't count")]
    public void TabFirstNumber()
    {
        Assert.Equal(new TrackedValue(181.9), ValueReader.FirstNumber("181.9 after a run")!.Value.Value);
        Assert.Equal(new TrackedValue(120, 80), ValueReader.FirstNumber("Reading: 120/80, a bit high")!.Value.Value);
        Assert.Equal(new TrackedValue(5), ValueReader.FirstNumber("ran 3rd, then 5 km")!.Value.Value);
        Assert.Equal(new TrackedValue(180), ValueReader.FirstNumber("##pulse=60 then 180")!.Value.Value);
        Assert.Null(ValueReader.FirstNumber("no numbers here"));
        Assert.Null(ValueReader.FirstNumber(""));
    }

    private static async Task<(Journal.Core.Services.JournalService Svc, FakeClock Clock)> JournalAsync(params (DateOnly Date, string Text, string? Tab, string? TabText)[] days)
    {
        var clock = new FakeClock();
        var (svc, _, _, _) = TestData.Service(clock);
        await svc.InitializeAsync();
        foreach (var (date, text, tab, tabText) in days)
        {
            var e = TestData.Entry(date, text);
            if (tab is not null) e.AddTab(Tag.Parse(tab)).Document.Blocks.Add(Documents.ParagraphBlock.OfText(tabText ?? ""));
            await svc.SaveAsync(e);
        }
        return (svc, clock);
    }

    [Fact(DisplayName = "TC-TRK-03 Values group by week (Sunday or Monday start) and month; the latest in a period counts; future days are ignored")]
    public async Task GroupsByPeriod()
    {
        // TestData.Today is Thursday 2026-10-15.
        var (svc, _) = await JournalAsync(
            (new(2026, 10, 4), "##weight=190", null, null),            // Sunday
            (new(2026, 10, 5), "##weight=189", null, null),            // Monday
            (new(2026, 10, 7), "##weight=188 ##weight=187", null, null), // the last in a day counts
            (new(2026, 10, 13), "Weighed in.", "Weight", "186 at the gym"),
            (new(2026, 10, 20), "##weight=150", null, null),           // future: ignored
            (new(2026, 9, 2), "##blood pressure=130/85", null, null),
            (new(2026, 9, 28), "##blood pressure=125/82", null, null),
            (new(2026, 10, 1), "##blood pressure=121/79", null, null));

        var sunday = svc.Tracking.Series(Weight, DayOfWeek.Sunday);
        Assert.Equal([new(new(2026, 10, 4), new(2026, 10, 7), new(187)), new(new(2026, 10, 11), new(2026, 10, 13), new(186))], sunday);

        var monday = svc.Tracking.Series(Weight, DayOfWeek.Monday);
        Assert.Equal([new DateOnly(2026, 9, 28), new(2026, 10, 5), new(2026, 10, 12)], monday.Select(p => p.PeriodStart));
        Assert.Equal([190.0, 187, 186], monday.Select(p => p.Value.First));

        var months = svc.Tracking.Series(Pressure, DayOfWeek.Sunday);
        Assert.Equal([new SeriesPoint(new(2026, 9, 1), new(2026, 9, 28), new(125, 82)), new(new(2026, 10, 1), new(2026, 10, 1), new(121, 79))], months);

        Assert.Equal([new DateOnly(2026, 10, 11)], svc.Tracking.Recent(Weight, DayOfWeek.Sunday, 1).Select(p => p.PeriodStart));
        Assert.Equal(new DateOnly(2026, 10, 11), TrackingService.PeriodStart(new(2026, 10, 15), MeasurePeriod.Weekly, DayOfWeek.Sunday));
        Assert.Equal(new DateOnly(2026, 10, 12), TrackingService.PeriodStart(new(2026, 10, 15), MeasurePeriod.Weekly, DayOfWeek.Monday));
        Assert.Equal(new DateOnly(2026, 10, 12), TrackingService.PeriodStart(new(2026, 10, 12), MeasurePeriod.Weekly, DayOfWeek.Monday));
    }

    [Fact(DisplayName = "TC-TRK-05 Past days count as soon as a measure exists; editing or deleting a value updates the graph")]
    public async Task FollowsChanges()
    {
        var (svc, _) = await JournalAsync((new(2026, 10, 1), "##mood score=7", null, null));
        var score = new Measure("Mood score");
        Assert.Equal([7.0], svc.Tracking.Series(score, DayOfWeek.Sunday).Select(p => p.Value.First));

        var day = (await svc.OpenDayAsync(new(2026, 10, 1))).Entry!;
        day.DefaultTab.Document.Blocks[0] = Documents.ParagraphBlock.OfText("##mood score=8");
        await svc.SaveAsync(day);
        Assert.Equal([8.0], svc.Tracking.Series(score, DayOfWeek.Sunday).Select(p => p.Value.First));

        day.DefaultTab.Document.Blocks[0] = Documents.ParagraphBlock.OfText("No score today");
        await svc.SaveAsync(day);
        Assert.Empty(svc.Tracking.Series(score, DayOfWeek.Sunday));

        await svc.SaveAsync(TestData.Entry(new(2026, 10, 2), "##mood score=6"));
        await svc.DeleteEntryAsync(new(2026, 10, 2));
        Assert.Empty(svc.Tracking.Series(score, DayOfWeek.Sunday));
    }

    [Fact(DisplayName = "TC-TRK-04 Measures: names checked, at most two on the main screen, saved through the store")]
    public async Task SavesMeasures()
    {
        var (svc, _) = await JournalAsync();
        Assert.Empty(svc.Tracking.Measures);
        int changed = 0;
        svc.Tracking.MeasuresChanged += (_, _) => changed++;

        await svc.Tracking.SaveMeasuresAsync([new(" Weight ", " lb ", MeasurePeriod.Weekly, true), Pressure with { ShowOnMain = true }]);
        Assert.Equal(["Weight", "Blood pressure"], svc.Tracking.Measures.Select(m => m.Name));
        Assert.Equal("lb", svc.Tracking.Measures[0].Unit);
        Assert.Equal(1, changed);

        Assert.NotNull(TrackingService.Problem([new("")]));
        Assert.NotNull(TrackingService.Problem([new("a#b")]));
        Assert.NotNull(TrackingService.Problem([new("Weight"), new("weight ")]));
        Assert.NotNull(TrackingService.Problem([new("A", ShowOnMain: true), new("B", ShowOnMain: true), new("C", ShowOnMain: true)]));
        Assert.Null(TrackingService.Problem([new("A", ShowOnMain: true), new("B", ShowOnMain: true), new("C")]));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.Tracking.SaveMeasuresAsync([new("x"), new("X")]));
        Assert.Equal(2, svc.Tracking.Measures.Count);
    }
}
