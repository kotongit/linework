using System.Globalization;

namespace Linework.Core.Plan;

/// <summary>Number is this repeat's lasting identity, which its lessons' ticks carry.</summary>
public sealed record PlanRepeat(int Number, int AfterSequenceIndex, int WeekCount);

/// <summary>One day of practice, by its place in the plan rather than its date.</summary>
public abstract record PlanDayRef;
/// <summary>Weekday is 0 for Monday.</summary>
public sealed record PlanRef(int SequenceIndex, int Weekday) : PlanDayRef;
/// <summary>The forever week repeats indefinitely, so only a date can tell its days apart.</summary>
public sealed record ForeverRef(string Day) : PlanDayRef;

public abstract record ScheduledDay
{
    public static readonly ScheduledDay NotStarted = new ScheduledNotStarted();
}
public sealed record ScheduledNotStarted : ScheduledDay;
public sealed record ScheduledPlanDay(int SequenceIndex, int WeekIndex, int Weekday, bool IsRepeat) : ScheduledDay;
public sealed record ScheduledForever(int Weekday) : ScheduledDay;

public readonly record struct SequenceEntry(int WeekIndex, bool IsRepeat, int RepeatNumber);

/// <summary>Maps calendar dates to plan days, taking repeated weeks into account.</summary>
public sealed class PlanSchedule
{
    static readonly string[] WeekdayNames = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    public PracticePlan Plan { get; }
    public IReadOnlyList<PlanRepeat> Repeats { get; }
    public IReadOnlyList<SequenceEntry> Sequence { get; }
    readonly DateOnly _start;

    public PlanSchedule(PracticePlan plan, DateOnly start, IReadOnlyList<PlanRepeat> repeats)
    {
        Plan = plan;
        Repeats = repeats;
        _start = start;
        var sequence = plan.Weeks.Select((_, i) => new SequenceEntry(i, false, 0)).ToList();
        foreach (var r in repeats)
        {
            if (r.AfterSequenceIndex < 0 || r.AfterSequenceIndex >= sequence.Count) continue;
            var first = Math.Max(r.AfterSequenceIndex - r.WeekCount + 1, 0);
            var copies = sequence.GetRange(first, Math.Max(r.AfterSequenceIndex - first + 1, 0))
                .Select(e => new SequenceEntry(e.WeekIndex, true, r.Number)).ToList();
            sequence.InsertRange(r.AfterSequenceIndex + 1, copies);
        }
        Sequence = sequence;
    }

    int DayOffset(DateOnly date) => date.DayNumber - _start.DayNumber;

    public ScheduledDay Scheduled(DateOnly date)
    {
        var offset = DayOffset(date);
        if (offset < 0) return ScheduledDay.NotStarted;
        int week = offset / 7, weekday = offset % 7;
        if (week >= Sequence.Count) return new ScheduledForever(weekday);
        return new ScheduledPlanDay(week, Sequence[week].WeekIndex, weekday, Sequence[week].IsRepeat);
    }

    public DateOnly DateOf(int sequenceIndex, int weekday) => _start.AddDays(sequenceIndex * 7 + weekday);

    /// <summary>The last Sunday of the sequence.</summary>
    public DateOnly EndDate => DateOf(Sequence.Count - 1, 6);

    /// <summary>What's planned on a date; null before the plan starts. The first Saturday of a month after the plan adds the ritual.</summary>
    public PlanDay? DayOn(DateOnly date)
    {
        switch (Scheduled(date))
        {
            case ScheduledPlanDay p:
                return Plan.Weeks[p.WeekIndex].Days[p.Weekday];
            case ScheduledForever f:
                var day = Plan.ForeverWeek[f.Weekday];
                if (f.Weekday != 5 || date.Day > 7) return day;
                var ritual = new PlanBlock(PlanBlockKind.Ritual, Plan.MonthlyRitual, 30, "Monthly ritual", Plan.MonthlyRitualBullets);
                return new PlanDay(false, [.. day.Blocks, ritual]);
            default:
                return null;
        }
    }

    public PlanSchedule Adding(PlanRepeat r) => new(Plan, _start, [.. Repeats, r]);

    public bool CanAddRepeat(int weekCount, DateOnly date) =>
        Scheduled(date) is ScheduledPlanDay p && p.SequenceIndex + 1 >= weekCount;

    /// <summary>A repeat can be undone until its first repeated date arrives.</summary>
    public bool CanUndoLastRepeat(DateOnly today) =>
        Repeats.Count > 0 && DateOf(Repeats[^1].AfterSequenceIndex + 1, 0) > today;

    // Lessons

    /// <summary>Where a date falls; null before Week 1.</summary>
    public PlanDayRef? RefOn(DateOnly date) => Scheduled(date) switch
    {
        ScheduledPlanDay p => new PlanRef(p.SequenceIndex, p.Weekday),
        ScheduledForever => new ForeverRef(PlanDayKey.Key(date)),
        _ => null,
    };

    /// <summary>The lesson to show today. A plan that hasn't begun yet shows its first day.</summary>
    public PlanDayRef TodayRef(DateOnly date) => RefOn(date) ?? new PlanRef(0, 0);

    public DateOnly DateOf(PlanDayRef r) => r switch
    {
        PlanRef p => DateOf(p.SequenceIndex, p.Weekday),
        ForeverRef f => PlanDayKey.Date(f.Day) ?? _start,
        _ => _start,
    };

    public PlanDay DayFor(PlanDayRef r) => r switch
    {
        PlanRef p => Plan.Weeks[Sequence[p.SequenceIndex].WeekIndex].Days[p.Weekday],
        _ => DayOn(DateOf(r)) ?? new PlanDay(true, []),
    };

    public string TitleFor(PlanDayRef r) => r switch
    {
        PlanRef p => $"Week {Plan.Weeks[Sequence[p.SequenceIndex].WeekIndex].Number.ToString(CultureInfo.InvariantCulture)} · {WeekdayNames[p.Weekday]}",
        ForeverRef f => $"Forever week · {WeekdayNames[ForeverWeekday(f.Day)]}",
        _ => "",
    };

    int ForeverWeekday(string day)
    {
        var offset = PlanDayKey.Date(day) is { } d ? DayOffset(d) : 0;
        return (offset % 7 + 7) % 7;
    }

    /// <summary>Names one block of one lesson, so its tick stays with it however the schedule shifts.</summary>
    public string TickKey(PlanDayRef r, PlanBlockKind kind)
    {
        if (r is ForeverRef f) return $"f-{f.Day}-{kind.Raw()}";
        var p = (PlanRef)r;
        var entry = Sequence[p.SequenceIndex];
        var week = Plan.Weeks[entry.WeekIndex].Number.ToString("00", CultureInfo.InvariantCulture);
        var name = WeekdayNames[p.Weekday][..3].ToLowerInvariant();
        var pass = entry.RepeatNumber == 0 ? "" : "-r" + entry.RepeatNumber.ToString(CultureInfo.InvariantCulture);
        // "This week and last", run during a repeat, can copy one week twice under one repeat.
        var copy = 1 + Sequence.Take(p.SequenceIndex).Count(e => e.WeekIndex == entry.WeekIndex && e.RepeatNumber == entry.RepeatNumber);
        if (copy > 1) pass += "c" + copy.ToString(CultureInfo.InvariantCulture);
        return $"w{week}-{name}-{kind.Raw()}{pass}";
    }

    // Today's practice

    /// <summary>One line naming the day's blocks, e.g. "Gestures, brush kit, one rule".</summary>
    public string PracticeLine(PlanDayRef r)
    {
        var day = DayFor(r);
        return day.IsRest ? "Rest" : string.Join(", ", day.Blocks.Select(b => b.Title).Distinct());
    }

    /// <summary>The first unticked block that runs as a timed session from photos.</summary>
    public PlanBlock? NextTimedBlock(PlanDayRef r, IReadOnlySet<string> ticks) =>
        DayFor(r).Blocks.FirstOrDefault(b => b.Timed != null && !ticks.Contains(TickKey(r, b.Kind)));
}
