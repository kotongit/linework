namespace Linework.Core.Plan;

public sealed record DayStat(DateOnly Day, int Sessions, int Seconds);

public static class PracticeCalendar
{
    /// <summary>Lower bounds (in drawing seconds) for heatmap levels 1–4.</summary>
    public static readonly int[] LevelThresholds = [1, 600, 1200, 2400];

    public static Dictionary<DateOnly, DayStat> DailyStats(IEnumerable<(DateOnly Day, int Seconds)> sessions)
    {
        var stats = new Dictionary<DateOnly, DayStat>();
        foreach (var (day, seconds) in sessions)
        {
            var s = stats.GetValueOrDefault(day) ?? new DayStat(day, 0, 0);
            stats[day] = s with { Sessions = s.Sessions + 1, Seconds = s.Seconds + seconds };
        }
        return stats;
    }

    public static int Level(int seconds) => LevelThresholds.Count(t => seconds >= t);

    /// <summary>Weeks columns of 7 days (Monday…Sunday); the last column holds today, later days are null.</summary>
    public static IReadOnlyList<IReadOnlyList<DateOnly?>> Grid(DateOnly today, int weeks = 52)
    {
        var thisMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var firstMonday = thisMonday.AddDays(-7 * (weeks - 1));
        return Enumerable.Range(0, weeks).Select(w =>
            (IReadOnlyList<DateOnly?>)Enumerable.Range(0, 7).Select(d =>
            {
                var day = firstMonday.AddDays(w * 7 + d);
                return day > today ? (DateOnly?)null : day;
            }).ToList()).ToList();
    }
}
