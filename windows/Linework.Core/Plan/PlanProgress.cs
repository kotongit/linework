namespace Linework.Core.Plan;

public enum DayStatus { Rest, Empty, Partial, Complete }
public enum Celebration { Day, Week, Book }

/// <summary>Everything derived from ticks and sessions. Nothing here is stored, so unticking corrects it.</summary>
public static class PlanProgress
{
    public static DayStatus Status(PlanDay day, IReadOnlySet<string> checkedKinds)
    {
        if (day.Blocks.Count == 0) return DayStatus.Rest;
        var done = day.Blocks.Count(b => checkedKinds.Contains(b.Kind.Raw()));
        return done == 0 ? DayStatus.Empty : done == day.Blocks.Count ? DayStatus.Complete : DayStatus.Partial;
    }

    /// <summary>Consecutive practice days ending today, or yesterday if today has no practice yet. Sundays never break it.</summary>
    public static int CurrentStreak(IReadOnlySet<DateOnly> practiceDays, DateOnly today)
    {
        var day = today;
        if (!practiceDays.Contains(day)) day = day.AddDays(-1);
        var count = 0;
        while (true)
        {
            if (practiceDays.Contains(day)) count++;
            else if (day.DayOfWeek != DayOfWeek.Sunday) return count;
            day = day.AddDays(-1);
        }
    }

    public static int LongestStreak(IReadOnlySet<DateOnly> practiceDays)
    {
        int best = 0, run = 0;
        DateOnly? last = null;
        foreach (var day in practiceDays.Order())
        {
            var gap = last is { } l ? day.DayNumber - l.DayNumber : (int?)null;
            var bridgesSunday = gap == 2 && last!.Value.AddDays(1).DayOfWeek == DayOfWeek.Sunday;
            run = gap == 1 || bridgesSunday ? run + 1 : 1;
            best = Math.Max(best, run);
            last = day;
        }
        return best;
    }

    public static int HeatmapSeconds(int sessionSeconds, int tickedMinutes) => Math.Max(sessionSeconds, tickedMinutes * 60);

    // Lessons

    public static DayStatus Status(PlanDayRef r, IReadOnlySet<string> ticks, PlanSchedule schedule)
    {
        var day = schedule.DayFor(r);
        var kinds = day.Blocks.Where(b => ticks.Contains(schedule.TickKey(r, b.Kind))).Select(b => b.Kind.Raw()).ToHashSet();
        return Status(day, kinds);
    }

    static bool IsWeekComplete(int sequenceIndex, IReadOnlySet<string> ticks, PlanSchedule schedule) =>
        Enumerable.Range(0, 7).All(weekday =>
            Status(new PlanRef(sequenceIndex, weekday), ticks, schedule) is DayStatus.Rest or DayStatus.Complete);

    /// <summary>The biggest thing a lesson's ticks complete; null unless that day itself is complete.</summary>
    public static Celebration? CelebrationFor(PlanDayRef r, IReadOnlySet<string> ticks, PlanSchedule schedule)
    {
        if (Status(r, ticks, schedule) != DayStatus.Complete) return null;
        if (r is not PlanRef p || !IsWeekComplete(p.SequenceIndex, ticks, schedule)) return Celebration.Day;
        var plan = schedule.Plan;
        var book = plan.Weeks[schedule.Sequence[p.SequenceIndex].WeekIndex].Book;
        var bookDone = Enumerable.Range(0, plan.Weeks.Count).Where(w => plan.Weeks[w].Book == book).All(week =>
            Enumerable.Range(0, schedule.Sequence.Count).Any(i => schedule.Sequence[i].WeekIndex == week && IsWeekComplete(i, ticks, schedule)));
        return bookDone ? Celebration.Book : Celebration.Week;
    }

    /// <summary>A practice day has a recorded session or at least one tick counting toward it.</summary>
    public static HashSet<DateOnly> PracticeDays(IEnumerable<PlanTick> ticks, IEnumerable<DateOnly> sessionDays)
    {
        var days = sessionDays.ToHashSet();
        foreach (var tick in ticks)
            if (PlanDayKey.Date(tick.CountsOn) is { } day) days.Add(day);
        return days;
    }

    /// <summary>How much was ticked on each day ticks count toward.</summary>
    public static Dictionary<string, (int Count, int Minutes)> TickSummary(IEnumerable<PlanTick> ticks, PlanSchedule schedule)
    {
        var minutes = new Dictionary<string, int>();
        for (var i = 0; i < schedule.Sequence.Count; i++)
            for (var weekday = 0; weekday < 7; weekday++)
            {
                var r = new PlanRef(i, weekday);
                foreach (var block in schedule.DayFor(r).Blocks) minutes[schedule.TickKey(r, block.Kind)] = block.Minutes;
            }
        var summary = new Dictionary<string, (int Count, int Minutes)>();
        foreach (var tick in ticks)
        {
            summary.TryGetValue(tick.CountsOn, out var entry);
            var added = minutes.TryGetValue(tick.Key, out var m) ? m : ForeverMinutes(tick.Key, schedule);
            summary[tick.CountsOn] = (entry.Count + 1, entry.Minutes + added);
        }
        return summary;
    }

    static int ForeverMinutes(string key, PlanSchedule schedule)
    {
        // "f-yyyy-MM-dd-kind"
        var parts = key.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[0] != "f") return 0;
        var r = new ForeverRef(string.Join('-', parts[1..4]));
        return schedule.DayFor(r).Blocks.FirstOrDefault(b => b.Kind.Raw() == parts[4])?.Minutes ?? 0;
    }
}
