namespace Journal.Core.Calendar;

/// <summary>Builds the 6×7 grid of dates shown by the month calendar (FR-CAL-02).</summary>
public static class MonthGrid
{
    public const int Cells = 42;

    public static IReadOnlyList<DateOnly> Build(int year, int month, DayOfWeek weekStart)
    {
        var first = new DateOnly(year, month, 1);
        int offset = ((int)first.DayOfWeek - (int)weekStart + 7) % 7;
        var start = first.AddDays(-offset);
        return Enumerable.Range(0, Cells).Select(start.AddDays).ToList();
    }

    public static IReadOnlyList<DayOfWeek> WeekdayOrder(DayOfWeek weekStart) =>
        Enumerable.Range(0, 7).Select(i => (DayOfWeek)(((int)weekStart + i) % 7)).ToList();
}
