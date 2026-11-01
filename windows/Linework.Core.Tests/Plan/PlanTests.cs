using System.Text.Json;
using Linework.Core.Session;
using Linework.Core.Plan;

namespace Linework.Core.Tests;

static class P
{
    public static DateOnly D(string key) => PlanDayKey.Date(key)!.Value;
    public static PlanSchedule S(string start = "2026-11-02", params PlanRepeat[] repeats) => new(PracticePlan.Shared, D(start), repeats);
    public static PlanRepeat R(int after, int count, int number = 0) => new(number, after, count);
    public static HashSet<string> AllKeys(PlanSchedule s, int seq)
    {
        var keys = new HashSet<string>();
        for (var w = 0; w < 7; w++)
        {
            var r = new PlanRef(seq, w);
            foreach (var b in s.DayFor(r).Blocks) keys.Add(s.TickKey(r, b.Kind));
        }
        return keys;
    }
}

public class PlanTickTests
{
    static DateOnly D(string k) => P.D(k);

    [Fact]
    public void CountsOnEarlierDay()
    {
        Assert.Equal("2026-11-10", PlanTick.CountsOnFor(D("2026-11-10"), D("2026-11-12")));
        Assert.Equal("2026-11-12", PlanTick.CountsOnFor(D("2026-11-20"), D("2026-11-12")));
        Assert.Equal("2026-11-12", PlanTick.CountsOnFor(D("2026-11-12"), D("2026-11-12")));
    }

    [Fact]
    public void KeysNameTheLesson()
    {
        Assert.Equal("w03-mon-focus", P.S().TickKey(new PlanRef(2, 0), PlanBlockKind.Focus));
        Assert.Equal("w01-sat-blockA", P.S().TickKey(new PlanRef(0, 5), PlanBlockKind.BlockA));
        Assert.Equal("f-2027-08-02-warmup", P.S().TickKey(new ForeverRef("2027-08-02"), PlanBlockKind.Warmup));
    }

    [Fact]
    public void TickStaysWithLessonAfterARepeat()
    {
        var before = P.S();
        var key = before.TickKey(new PlanRef(5, 1), PlanBlockKind.Focus);
        Assert.Equal(key, before.Adding(P.R(1, 1, 1)).TickKey(new PlanRef(6, 1), PlanBlockKind.Focus));
    }

    [Fact]
    public void RepeatedLessonHasItsOwnKey()
    {
        var s = P.S(repeats: P.R(1, 1, 3));
        Assert.Equal("w02-mon-focus", s.TickKey(new PlanRef(1, 0), PlanBlockKind.Focus));
        Assert.Equal("w02-mon-focus-r3", s.TickKey(new PlanRef(2, 0), PlanBlockKind.Focus));
    }

    [Fact]
    public void ARepeatOfARepeatKeepsLessonsApart()
    {
        var s = P.S(repeats: [P.R(4, 1, 1), P.R(5, 2, 2)]);
        var keys = Enumerable.Range(4, 4).Select(i => s.TickKey(new PlanRef(i, 0), PlanBlockKind.Focus)).ToList();
        Assert.Equal(4, keys.Distinct().Count());
        Assert.Equal("w05-mon-focus", keys[0]);
        Assert.Equal("w05-mon-focus-r2", keys[2]);
        Assert.Equal("w05-mon-focus-r2c2", keys[3]);
    }

    [Fact]
    public void AnyStartMondayMapsWeeks()
    {
        var s = P.S("2030-03-04");
        Assert.Equal(new PlanRef(0, 0), s.RefOn(D("2030-03-04")));
        Assert.Equal(new PlanRef(1, 1), s.RefOn(D("2030-03-12")));
        Assert.Equal("Week 2 · Tuesday", s.TitleFor(new PlanRef(1, 1)));
    }

    [Fact]
    public void StartStillAheadShowsWeekOneMonday()
    {
        var s = P.S("2030-03-04");
        Assert.Null(s.RefOn(D("2030-01-01")));
        Assert.Equal(new PlanRef(0, 0), s.TodayRef(D("2030-01-01")));
    }

    [Fact]
    public void AfterTheLastWeekIsForever()
    {
        var s = P.S();
        var r = s.RefOn(D("2027-08-02"))!;
        Assert.Equal(new ForeverRef("2027-08-02"), r);
        Assert.Equal("Forever week · Monday", s.TitleFor(r));
        Assert.NotEmpty(s.DayFor(r).Blocks);
    }

    [Fact]
    public void DateOfRefRoundTrips()
    {
        Assert.Equal(D("2026-11-11"), P.S().DateOf(new PlanRef(1, 2)));
        Assert.Equal(D("2027-08-02"), P.S().DateOf(new ForeverRef("2027-08-02")));
    }

    [Fact]
    public void FutureTickNeverCountsOnAFutureDay()
    {
        var today = D("2026-11-12");
        var tick = new PlanTick("w10-mon-focus", PlanTick.CountsOnFor(D("2027-01-04"), today), DateTimeOffset.UnixEpoch);
        var days = PlanProgress.PracticeDays([tick], []);
        Assert.Equal([today], days);
        Assert.Equal(1, PlanProgress.CurrentStreak(days, today));
    }

    [Fact]
    public void MissedLessonMendsStreak()
    {
        var ticks = new[] { "2026-11-10", "2026-11-11", "2026-11-12" }.Select(k => new PlanTick(k, k, DateTimeOffset.UnixEpoch)).ToList();
        Assert.Equal(3, PlanProgress.CurrentStreak(PlanProgress.PracticeDays(ticks, []), D("2026-11-12")));
    }

    [Fact]
    public void StatusAndCelebrationReadLessonKeys()
    {
        var s = P.S();
        var r = new PlanRef(1, 0);
        var keys = s.DayFor(r).Blocks.Select(b => s.TickKey(r, b.Kind)).ToHashSet();
        Assert.Equal(DayStatus.Partial, PlanProgress.Status(r, new HashSet<string> { keys.First() }, s));
        Assert.Equal(DayStatus.Complete, PlanProgress.Status(r, keys, s));
        Assert.Equal(Celebration.Day, PlanProgress.CelebrationFor(r, keys, s));
        Assert.Null(PlanProgress.CelebrationFor(r, new HashSet<string> { keys.First() }, s));
    }

    [Fact]
    public void FinishingAWeekAheadStillCelebrates() =>
        Assert.Equal(Celebration.Week, PlanProgress.CelebrationFor(new PlanRef(3, 0), P.AllKeys(P.S(), 3), P.S()));

    [Fact]
    public void TicksAddUpByTheDayTheyCountOn()
    {
        var s = P.S();
        var r = new PlanRef(1, 0);
        var ticks = s.DayFor(r).Blocks.Select(b => new PlanTick(s.TickKey(r, b.Kind), "2026-11-09", DateTimeOffset.UnixEpoch)).ToList();
        var sum = PlanProgress.TickSummary(ticks, s)["2026-11-09"];
        Assert.Equal(ticks.Count, sum.Count);
        Assert.Equal(s.DayFor(r).Blocks.Sum(b => b.Minutes), sum.Minutes);
    }

    [Fact]
    public void ForeverTicksHaveMinutesToo()
    {
        var s = P.S();
        var tick = new PlanTick(s.TickKey(new ForeverRef("2027-08-02"), PlanBlockKind.Warmup), "2027-08-02", DateTimeOffset.UnixEpoch);
        Assert.Equal(5, PlanProgress.TickSummary([tick], s)["2027-08-02"].Minutes);
    }

    [Fact]
    public void NextTimedBlockUsesLessonKeys()
    {
        var s = P.S();
        var r = new PlanRef(0, 1);
        Assert.Equal(PlanBlockKind.Warmup, s.NextTimedBlock(r, new HashSet<string>())!.Kind);
        Assert.Null(s.NextTimedBlock(r, new HashSet<string> { s.TickKey(r, PlanBlockKind.Warmup) }));
    }
}

public class PlanScheduleTests
{
    static DateOnly D(string k) => P.D(k);

    [Fact]
    public void BeforeStart()
    {
        Assert.Equal(ScheduledDay.NotStarted, P.S().Scheduled(D("2026-11-01")));
        Assert.Null(P.S().DayOn(D("2026-11-01")));
    }

    [Fact]
    public void FirstDay() => Assert.Equal(new ScheduledPlanDay(0, 0, 0, false), P.S().Scheduled(D("2026-11-02")));

    [Fact]
    public void SundayMapping()
    {
        Assert.Equal(new ScheduledPlanDay(0, 0, 6, false), P.S().Scheduled(D("2026-11-08")));
        Assert.True(P.S().DayOn(D("2026-11-08"))!.IsRest);
    }

    [Fact]
    public void LastDay()
    {
        var s = P.S();
        Assert.Equal(new ScheduledPlanDay(37, 37, 6, false), s.Scheduled(D("2027-07-25")));
        Assert.Equal(new ScheduledForever(0), s.Scheduled(D("2027-07-26")));
        Assert.Equal(D("2027-07-25"), s.EndDate);
    }

    [Fact]
    public void RepeatOne()
    {
        var s = P.S(repeats: P.R(4, 1));
        Assert.Equal(new ScheduledPlanDay(4, 4, 6, false), s.Scheduled(D("2026-12-06")));
        Assert.Equal(new ScheduledPlanDay(5, 4, 0, true), s.Scheduled(D("2026-12-07")));
        Assert.Equal(new ScheduledPlanDay(6, 5, 0, false), s.Scheduled(D("2026-12-14")));
        Assert.Equal(D("2027-08-01"), s.EndDate);
    }

    [Fact]
    public void RepeatTwo()
    {
        var s = P.S(repeats: P.R(4, 2));
        Assert.True(s.Sequence[5].WeekIndex == 3 && s.Sequence[5].IsRepeat);
        Assert.True(s.Sequence[6].WeekIndex == 4 && s.Sequence[6].IsRepeat);
        Assert.True(s.Sequence[7].WeekIndex == 5 && !s.Sequence[7].IsRepeat);
        Assert.Equal(D("2027-08-08"), s.EndDate);
    }

    [Fact]
    public void SeveralRepeats()
    {
        var s = P.S(repeats: [P.R(1, 1), P.R(2, 2)]);
        Assert.Equal([0, 1, 1, 1, 1, 2, 3], s.Sequence.Take(7).Select(e => e.WeekIndex));
        Assert.Equal(41, s.Sequence.Count);
        Assert.Equal(2, s.Repeats.Count);
    }

    [Fact]
    public void AddingMatchesInit() => Assert.Equal(P.S(repeats: P.R(3, 1)).EndDate, P.S().Adding(P.R(3, 1)).EndDate);

    [Fact]
    public void DateForSequence() => Assert.Equal(D("2026-11-11"), P.S().DateOf(1, 2));

    [Fact]
    public void CanAdd()
    {
        var s = P.S();
        Assert.True(s.CanAddRepeat(1, D("2026-11-03")));
        Assert.False(s.CanAddRepeat(2, D("2026-11-03")));
        Assert.True(s.CanAddRepeat(2, D("2026-11-10")));
        Assert.False(s.CanAddRepeat(1, D("2026-10-31")));
        Assert.False(s.CanAddRepeat(1, D("2027-08-01")));
    }

    [Fact]
    public void CanUndo()
    {
        Assert.False(P.S().CanUndoLastRepeat(D("2026-11-03")));
        var s = P.S(repeats: P.R(0, 1));
        Assert.True(s.CanUndoLastRepeat(D("2026-11-08")));
        Assert.False(s.CanUndoLastRepeat(D("2026-11-09")));
    }

    [Fact]
    public void ForeverRitual()
    {
        var s = P.S();
        Assert.Equal(PlanBlockKind.Ritual, s.DayOn(D("2027-08-07"))!.Blocks[^1].Kind);
        Assert.Equal(30, s.DayOn(D("2027-08-07"))!.Blocks[^1].Minutes);
        Assert.DoesNotContain(s.DayOn(D("2027-08-14"))!.Blocks, b => b.Kind == PlanBlockKind.Ritual);
        Assert.True(s.DayOn(D("2027-08-08"))!.IsRest);
    }

    [Fact]
    public void KeysRoundTrip()
    {
        Assert.Equal("2026-11-02", PlanDayKey.Key(new DateOnly(2026, 11, 2)));
        Assert.Equal(new DateOnly(2026, 11, 2), PlanDayKey.Date("2026-11-02"));
        Assert.Null(PlanDayKey.Date("nope"));
        Assert.Null(PlanDayKey.Date("2026-13-45"));
    }
}

public class PlanProgressTests
{
    static DateOnly D(string k) => P.D(k);
    static HashSet<DateOnly> Days(params string[] keys) => keys.Select(D).ToHashSet();
    static PracticePlan Plan => PracticePlan.Shared;

    [Fact]
    public void Status()
    {
        var day = Plan.Weeks[1].Days[0];
        Assert.Equal(DayStatus.Rest, PlanProgress.Status(Plan.Weeks[1].Days[6], new HashSet<string>()));
        Assert.Equal(DayStatus.Empty, PlanProgress.Status(day, new HashSet<string>()));
        Assert.Equal(DayStatus.Partial, PlanProgress.Status(day, new HashSet<string> { "warmup" }));
        Assert.Equal(DayStatus.Complete, PlanProgress.Status(day, new HashSet<string> { "warmup", "focus", "check", "extra" }));
        Assert.Equal(DayStatus.Empty, PlanProgress.Status(day, new HashSet<string> { "extra" }));
    }

    [Fact]
    public void StreakSundayBridge()
    {
        Assert.Equal(2, PlanProgress.CurrentStreak(Days("2026-11-07", "2026-11-09"), D("2026-11-09")));
        Assert.Equal(3, PlanProgress.CurrentStreak(Days("2026-11-07", "2026-11-08", "2026-11-09"), D("2026-11-09")));
    }

    [Fact]
    public void StreakNotYetToday()
    {
        var days = Days("2026-11-09", "2026-11-10");
        Assert.Equal(2, PlanProgress.CurrentStreak(days, D("2026-11-11")));
        Assert.Equal(0, PlanProgress.CurrentStreak(days, D("2026-11-12")));
    }

    [Fact]
    public void StreakBreak()
    {
        Assert.Equal(1, PlanProgress.CurrentStreak(Days("2026-11-09", "2026-11-11"), D("2026-11-11")));
        Assert.Equal(2, PlanProgress.CurrentStreak(Days("2026-11-06", "2026-11-07"), D("2026-11-08")));
    }

    [Fact]
    public void Longest()
    {
        Assert.Equal(0, PlanProgress.LongestStreak(new HashSet<DateOnly>()));
        Assert.Equal(3, PlanProgress.LongestStreak(Days("2026-11-06", "2026-11-07", "2026-11-09", "2026-11-20")));
    }

    [Fact]
    public void FinishingABookIsTheBiggestCelebration() =>
        Assert.Equal(Celebration.Book, PlanProgress.CelebrationFor(new PlanRef(0, 0), P.AllKeys(P.S(), 0), P.S()));

    [Fact]
    public void HeatmapMax()
    {
        Assert.Equal(1800, PlanProgress.HeatmapSeconds(1800, 30));
        Assert.Equal(1500, PlanProgress.HeatmapSeconds(300, 25));
        Assert.Equal(0, PlanProgress.HeatmapSeconds(0, 0));
    }
}

public class PracticeCalendarTests
{
    [Theory]
    [InlineData(0, 0), InlineData(30, 1), InlineData(599, 1), InlineData(600, 2), InlineData(1199, 2), InlineData(1200, 3), InlineData(2399, 3), InlineData(2400, 4)]
    public void Levels(int seconds, int level) => Assert.Equal(level, PracticeCalendar.Level(seconds));

    [Fact]
    public void DailyStatsBucketsByDay()
    {
        var d21 = new DateOnly(2026, 10, 21);
        var d22 = new DateOnly(2026, 10, 22);
        var stats = PracticeCalendar.DailyStats([(d21, 120), (d22, 300), (d22, 60)]);
        Assert.Equal(2, stats.Count);
        Assert.Equal(new DayStat(d22, 2, 360), stats[d22]);
        Assert.Equal(120, stats[d21].Seconds);
    }

    [Fact]
    public void GridShape()
    {
        var today = new DateOnly(2026, 10, 22);
        var g = PracticeCalendar.Grid(today);
        Assert.Equal(52, g.Count);
        Assert.All(g, c => Assert.Equal(7, c.Count));
        Assert.Equal(today, g[51][3]);
        Assert.Equal(new DateOnly(2026, 10, 19), g[51][0]);
        Assert.Null(g[51][4]);
        Assert.Null(g[51][6]);
        Assert.Equal(new DateOnly(2026, 10, 18), g[50][6]);
    }
}

public class BenchmarkScheduleTests
{
    static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
    static List<DateTimeOffset> Sessions(int n) => Enumerable.Range(1, n).Select(i => T0.AddHours(i)).ToList();

    [Fact]
    public void DueAfterInterval()
    {
        Assert.False(BenchmarkSchedule.IsDue(T0, 10, Sessions(9)));
        Assert.True(BenchmarkSchedule.IsDue(T0, 10, Sessions(10)));
        Assert.Equal(2, BenchmarkSchedule.SessionsSince(T0, [T0, T0.AddSeconds(-5), .. Sessions(2)]));
    }

    [Fact]
    public void IntervalZeroTreatedAsOne()
    {
        Assert.False(BenchmarkSchedule.IsDue(T0, 0, []));
        Assert.True(BenchmarkSchedule.IsDue(T0, 0, Sessions(1)));
    }
}

public class PlanSummaryTests
{
    static DateOnly D(string k) => P.D(k);
    static PracticePlan Plan => PracticePlan.Shared;

    [Fact]
    public void EveryBlockSummarized()
    {
        foreach (var b in Plan.Weeks.SelectMany(w => w.Days).Concat(Plan.ForeverWeek).SelectMany(d => d.Blocks))
        {
            Assert.True(b.Title.Length is > 0 and <= 28, b.Title);
            Assert.InRange(b.Bullets.Count, 1, 3);
            Assert.All(b.Bullets, x => Assert.True(x.Length is > 0 and <= 40, x));
        }
    }

    [Fact]
    public void RitualBullets()
    {
        var ritual = P.S().DayOn(D("2027-08-07"))!.Blocks[^1];
        Assert.Equal("Monthly ritual", ritual.Title);
        Assert.Equal(Plan.MonthlyRitualBullets, ritual.Bullets);
    }

    [Fact]
    public void FullTextKept()
    {
        var focus = Plan.Weeks[0].Days[1].Blocks.First(b => b.Kind == PlanBlockKind.Focus);
        Assert.StartsWith("Brush kit (Ch. 2)", focus.Text);
        Assert.Equal("Brush kit", focus.Title);
        Assert.Contains("Set up 5 brushes", focus.Bullets);
    }

    [Fact]
    public void PracticeLine()
    {
        var s = P.S();
        Assert.Equal("Rest", s.PracticeLine(s.TodayRef(D("2026-11-08"))));
        Assert.Contains("Brush kit", s.PracticeLine(s.TodayRef(D("2026-11-03"))));
        Assert.Contains("Set up Procreate", s.PracticeLine(s.TodayRef(D("2026-11-01"))));
    }

    [Fact]
    public void NextTimed()
    {
        var s = P.S();
        var monday = s.TodayRef(D("2026-11-09"));
        var focus = s.DayFor(monday).Blocks.First(b => b.Kind == PlanBlockKind.Focus);
        Assert.NotNull(focus.Timed);
        Assert.Same(focus, s.NextTimedBlock(monday, new HashSet<string>()));
        Assert.Null(s.NextTimedBlock(monday, new HashSet<string> { s.TickKey(monday, PlanBlockKind.Focus) }));
        Assert.Null(s.NextTimedBlock(s.TodayRef(D("2026-11-08")), new HashSet<string>()));
    }
}

/// <summary>Values the Mac code worked out, checked here so the two apps can't drift.</summary>
public class SharedExpectationTests
{
    [Fact]
    public void MatchesSharedExpectations()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Repo.Fixture("plan-expectations.json")));
        var e = doc.RootElement;
        var repeats = e.GetProperty("repeats").EnumerateArray()
            .Select(r => new PlanRepeat(r.GetProperty("number").GetInt32(), r.GetProperty("afterSequenceIndex").GetInt32(), r.GetProperty("weekCount").GetInt32())).ToArray();
        var s = P.S(e.GetProperty("start").GetString()!, repeats);

        foreach (var c in e.GetProperty("refs").EnumerateObject())
        {
            var actual = s.RefOn(P.D(c.Name)) switch
            {
                null => "none",
                PlanRef p => $"plan:{p.SequenceIndex}:{p.Weekday}",
                ForeverRef f => $"forever:{f.Day}",
                _ => "?",
            };
            Assert.Equal(c.Value.GetString(), actual);
        }
        foreach (var c in e.GetProperty("titles").EnumerateObject()) Assert.Equal(c.Value.GetString(), s.TitleFor(s.TodayRef(P.D(c.Name))));
        foreach (var c in e.GetProperty("practiceLines").EnumerateObject()) Assert.Equal(c.Value.GetString(), s.PracticeLine(s.TodayRef(P.D(c.Name))));
        foreach (var c in e.GetProperty("tickKeys").EnumerateObject())
        {
            var parts = c.Name.Split('/');
            PlanDayRef r = parts[0] == "forever" ? new ForeverRef(parts[1]) : new PlanRef(int.Parse(parts[0]), int.Parse(parts[1]));
            Assert.Equal(c.Value.GetString(), s.TickKey(r, PlanBlockKinds.Parse(parts[2])));
        }
        Assert.Contains(e.GetProperty("tickKeys").EnumerateObject(), c => c.Value.GetString()!.EndsWith("-r2c2"));
        Assert.Equal(e.GetProperty("endDate").GetString(), PlanDayKey.Key(s.EndDate));

        foreach (var c in e.GetProperty("streaks").EnumerateArray())
        {
            var days = c.GetProperty("days").EnumerateArray().Select(d => P.D(d.GetString()!)).ToHashSet();
            Assert.Equal(c.GetProperty("current").GetInt32(), PlanProgress.CurrentStreak(days, P.D(c.GetProperty("today").GetString()!)));
            Assert.Equal(c.GetProperty("longest").GetInt32(), PlanProgress.LongestStreak(days));
        }

        var ticks = e.GetProperty("ticks").EnumerateArray()
            .Select(t => new PlanTick(t.GetProperty("key").GetString()!, t.GetProperty("countsOn").GetString()!, DateTimeOffset.UnixEpoch)).ToList();
        var summary = PlanProgress.TickSummary(ticks, s);
        var expected = e.GetProperty("tickSummary").EnumerateObject().ToList();
        Assert.Equal(expected.Count, summary.Count);
        foreach (var c in expected)
        {
            Assert.Equal(c.Value.GetProperty("count").GetInt32(), summary[c.Name].Count);
            Assert.Equal(c.Value.GetProperty("minutes").GetInt32(), summary[c.Name].Minutes);
        }
        foreach (var h in e.GetProperty("heatmap").EnumerateArray())
            Assert.Equal(h[2].GetInt32(), PlanProgress.HeatmapSeconds(h[0].GetInt32(), h[1].GetInt32()));
    }
}
