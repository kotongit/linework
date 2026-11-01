namespace Linework.Core.Plan;

public static class BenchmarkSchedule
{
    /// <summary>Sessions that started strictly after the date.</summary>
    public static int SessionsSince(DateTimeOffset date, IEnumerable<DateTimeOffset> sessionDates) => sessionDates.Count(d => d > date);

    /// <summary>Due once the sessions since the last attempt (or since marking) reach the interval.</summary>
    public static bool IsDue(DateTimeOffset since, int interval, IEnumerable<DateTimeOffset> sessionDates) =>
        SessionsSince(since, sessionDates) >= Math.Max(interval, 1);
}
