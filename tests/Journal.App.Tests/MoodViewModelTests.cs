using Journal.App.ViewModels;
using Journal.Core.Domain;

namespace Journal.App.Tests;

/// <summary>ISS-013: choosing a face on the day's heading, the calendar tint and the counts under the calendar.</summary>
public class MoodViewModelTests
{
    [Fact(DisplayName = "TC-MOOD-05 Choosing a face saves at once and updates the calendar and the counts")]
    public void ChooseMood() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var main = await h.MainAsync();
        var entry = main.CurrentEntry!;
        Assert.True(entry.CanSetMood);
        Assert.Equal([Mood.Sad, Mood.Neutral, Mood.Happy], entry.MoodChoices.Select(c => c.Mood));
        Assert.Equal("Happy day", entry.MoodChoices[2].Label);

        await entry.ChooseMoodCommand.ExecuteAsync(entry.MoodChoices[2]);
        Assert.Equal(Mood.Happy, entry.Mood);
        Assert.True(entry.MoodChoices[2].IsSelected);
        Assert.False(entry.MoodChoices[0].IsSelected);
        Assert.Equal(Mood.Happy, h.Journal.Catalog.Get(h.Clock.Today)!.Mood);   // saved without waiting for autosave

        var cell = main.Calendar.Days.Single(d => d.Date == h.Clock.Today);
        Assert.Equal(Mood.Happy, cell.Mood);
        Assert.EndsWith(", happy day", cell.AutomationName);
        Assert.Equal(new MoodCounts(0, 0, 1), main.Moods.Last30Days);
        Assert.Equal(new MoodCounts(0, 0, 1), main.Moods.Last12Months);

        // Another face replaces it; the same face again clears it.
        await entry.ChooseMoodCommand.ExecuteAsync(entry.MoodChoices[0]);
        Assert.Equal(Mood.Sad, main.Calendar.Days.Single(d => d.Date == h.Clock.Today).Mood);
        Assert.Equal(new MoodCounts(1, 0, 0), main.Moods.Last30Days);
        await entry.ChooseMoodCommand.ExecuteAsync(entry.MoodChoices[0]);
        Assert.Equal(Mood.None, entry.Mood);
        Assert.Equal(Mood.None, main.Calendar.Days.Single(d => d.Date == h.Clock.Today).Mood);
        Assert.Equal(new MoodCounts(0, 0, 0), main.Moods.Last30Days);
        Assert.False(h.Journal.HasEntry(h.Clock.Today));   // nothing else on the day, so it's removed again
    });

    [Fact(DisplayName = "TC-MOOD-01 Future days can't be given a mood")]
    public void FutureDay() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var main = await h.MainAsync();
        await main.SelectDateAsync(h.Clock.Today.AddDays(3));
        var entry = main.CurrentEntry!;
        Assert.False(entry.CanSetMood);
        Assert.Contains("once it arrives", entry.MoodHint);
        await entry.ChooseMoodCommand.ExecuteAsync(entry.MoodChoices[2]);
        Assert.Equal(Mood.None, entry.Mood);
        Assert.False(h.Journal.HasEntry(h.Clock.Today.AddDays(3)));
    });

    [Fact(DisplayName = "TC-MOOD-03 Calendar cells carry each day's mood; days without one have none")]
    public void CalendarCells() => Sta.Run(async () =>
    {
        var h = new AppHarness();
        var main = await h.MainAsync();
        foreach (var (offset, mood) in new[] { (-1, Mood.Sad), (-2, Mood.Neutral), (-3, Mood.Happy) })
        {
            await main.SelectDateAsync(h.Clock.Today.AddDays(offset));
            await main.CurrentEntry!.ChooseMoodCommand.ExecuteAsync(main.CurrentEntry.MoodChoices.Single(c => c.Mood == mood));
        }
        Assert.Equal(Mood.Sad, Cell(-1).Mood);
        Assert.Equal(Mood.Neutral, Cell(-2).Mood);
        Assert.Equal(Mood.Happy, Cell(-3).Mood);
        Assert.Equal(Mood.None, Cell(-4).Mood);
        Assert.True(Cell(-1).HasEntry);
        Assert.Equal(new MoodCounts(1, 1, 1), main.Moods.Last30Days);

        // Reopening a day shows its saved face.
        await main.SelectDateAsync(h.Clock.Today.AddDays(-3));
        Assert.True(main.CurrentEntry!.MoodChoices.Single(c => c.Mood == Mood.Happy).IsSelected);

        DayCellViewModel Cell(int offset) => main.Calendar.Days.Single(d => d.Date == h.Clock.Today.AddDays(offset));
    });
}
