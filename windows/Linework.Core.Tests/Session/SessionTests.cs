using System.Globalization;
using Linework.Core.Library;
using Linework.Core.Plan;
using Linework.Core.Session;

namespace Linework.Core.Tests;

static class Sess
{
    public static Pose Pose(int s, int? bench = null, int? step = null) => new(Guid.NewGuid(), s, bench, step);
    public static List<Guid> Ids(int n) => Enumerable.Range(0, n).Select(_ => Guid.NewGuid()).ToList();
}

public class SessionRunnerTests
{
    static SessionRunner Run(params Pose[] poses) => new(poses);
    static Pose P(int s, int? bench = null, int? step = null) => Sess.Pose(s, bench, step);

    [Fact]
    public void GetReadyThenPose()
    {
        var r = Run(P(30));
        Assert.Equal(RunnerPhase.GetReady, r.Phase);
        Assert.Equal(3, r.GetReadyRemaining);
        r.Tick(3.0);
        Assert.Equal(RunnerPhase.Pose, r.Phase);
        Assert.Equal(30, r.Remaining);
    }

    [Fact]
    public void PauseExcludesTime()
    {
        var r = Run(P(30));
        r.Tick(3); r.Tick(1);
        r.TogglePause();
        r.Tick(10);
        Assert.Equal(1, r.DrawingSeconds);
        Assert.Equal(29, r.Remaining);
    }

    [Fact]
    public void TickEventsInLastFiveSeconds()
    {
        var r = Run(P(30), P(30));
        r.Tick(3);
        var ticks = 0;
        for (var i = 0; i < 30; i++)
        {
            var e = r.Tick(1.0);
            if (e.HasFlag(RunnerEvent.Tick)) ticks++;
            if (e.HasFlag(RunnerEvent.PoseChanged)) break;
        }
        Assert.Equal(5, ticks);
        Assert.Equal(1, r.Index);
    }

    [Fact]
    public void PreviousRestartsPose()
    {
        var r = Run(P(30), P(30));
        r.Tick(3); r.Tick(10);
        r.Next();
        r.Tick(2);
        r.Previous();
        Assert.Equal(0, r.Index);
        Assert.Equal(30, r.Remaining);
        Assert.Equal(12, r.DrawingSeconds);
    }

    [Fact]
    public void BenchmarkAwaitsAttempt()
    {
        var r = Run(P(5, bench: 1), P(30));
        r.Tick(3);
        Assert.True(r.Tick(5).HasFlag(RunnerEvent.BenchmarkEnded));
        Assert.Equal(RunnerPhase.AwaitingAttempt, r.Phase);
        r.Tick(100);
        Assert.Equal(RunnerPhase.AwaitingAttempt, r.Phase);
        Assert.Equal(0, r.Index);
        r.AttemptHandled();
        Assert.Equal(1, r.Index);
        Assert.Equal(RunnerPhase.Pose, r.Phase);
    }

    [Fact]
    public void SkippingBenchmarkAlsoAwaitsAttempt()
    {
        var r = Run(P(5, bench: 1), P(30));
        r.Tick(3);
        Assert.True(r.Next().HasFlag(RunnerEvent.BenchmarkEnded));
        Assert.Equal(RunnerPhase.AwaitingAttempt, r.Phase);
    }

    [Fact]
    public void StepChangeGetsReady()
    {
        var r = Run(P(5, step: 0), P(30, step: 1));
        r.Tick(3);
        var e = r.Tick(5);
        Assert.True(e.HasFlag(RunnerEvent.StepChanged) && e.HasFlag(RunnerEvent.PoseChanged));
        Assert.Equal(RunnerPhase.GetReady, r.Phase);
        Assert.Equal(3, r.GetReadyRemaining);
    }

    [Fact]
    public void DrawingStartIsAnnouncedAfterCountdown()
    {
        var r = Run(P(5, step: 0), P(30, step: 1));
        Assert.Equal(RunnerEvent.None, r.Tick(1));
        Assert.True(r.Tick(2).HasFlag(RunnerEvent.DrawingStarted));
        Assert.False(r.Tick(5).HasFlag(RunnerEvent.DrawingStarted));
        Assert.True(r.Tick(3).HasFlag(RunnerEvent.DrawingStarted));
    }

    [Fact]
    public void NoCountdownMeansNoStartEvent()
    {
        var r = Run(P(5), P(5));
        r.Tick(3);
        var e = r.Tick(5);
        Assert.True(e.HasFlag(RunnerEvent.PoseChanged) && !e.HasFlag(RunnerEvent.DrawingStarted));
    }

    [Fact]
    public void ShouldRecordThreshold()
    {
        var a = Run(P(30));
        a.Tick(3); a.Tick(4.9);
        Assert.False(a.ShouldRecord);
        var b = Run(P(30));
        b.Tick(3); b.Tick(5.0);
        Assert.True(b.ShouldRecord);
    }

    [Fact]
    public void FinishAfterLast()
    {
        var r = Run(P(30));
        r.Tick(3);
        Assert.True(r.Next().HasFlag(RunnerEvent.Finished));
        Assert.Equal(RunnerPhase.Finished, r.Phase);
    }

    [Fact]
    public void PreviousDoesNotReturnToHandledBenchmark()
    {
        var r = Run(P(5, bench: 1), P(30), P(30));
        r.Tick(3); r.Tick(5);
        r.AttemptHandled();
        r.Tick(4);
        r.Previous();
        Assert.Equal(1, r.Index);
        Assert.Equal(30, r.Remaining);
        Assert.Equal(RunnerPhase.Pose, r.Phase);
    }

    [Fact]
    public void PreviousDuringOpeningCountdownKeepsCountdown()
    {
        var r = Run(P(30), P(30));
        r.Tick(1);
        Assert.Equal(RunnerEvent.None, r.Previous());
        Assert.Equal(RunnerPhase.GetReady, r.Phase);
        Assert.Equal(2, r.GetReadyRemaining);
    }

    [Fact]
    public void NaturalFinishByTimer()
    {
        var r = Run(P(5));
        r.Tick(3); r.Tick(5);
        Assert.True(r.CompletedNaturally);
    }

    [Fact]
    public void NaturalFinishBySkipping()
    {
        var r = Run(P(30), P(30));
        r.Tick(3); r.Next(); r.Next();
        Assert.True(r.CompletedNaturally);
    }

    [Fact]
    public void EscIsNotNatural()
    {
        var r = Run(P(30));
        r.Tick(3); r.End();
        Assert.False(r.CompletedNaturally);
        var done = Run(P(5));
        done.Tick(3); done.Tick(5); done.End();
        Assert.True(done.CompletedNaturally);
    }

    [Fact]
    public void RunningIsNotNatural()
    {
        var r = Run(P(30));
        r.Tick(3); r.Tick(1);
        Assert.False(r.CompletedNaturally);
        Assert.False(Run().CompletedNaturally);
    }

    [Fact]
    public void EmptyIsFinished() => Assert.Equal(RunnerPhase.Finished, Run().Phase);
}

public class SessionQueueTests
{
    static readonly Random Rng = new(1);

    [Fact]
    public void SimpleUsesAllOnce()
    {
        var pool = Sess.Ids(3);
        var q = QueueBuilder.Build(pool, [], new SimpleMode(60, null), false, [], Rng);
        Assert.Equal(pool, q.Poses.Select(p => p.ImageID));
        Assert.All(q.Poses, p => { Assert.Equal(60, p.Seconds); Assert.Null(p.StepIndex); });
        Assert.False(q.RepeatedImages);
    }

    [Fact]
    public void SimpleMaxPosesRepeats()
    {
        var q = QueueBuilder.Build(Sess.Ids(2), [], new SimpleMode(30, 5), false, [], Rng);
        Assert.Equal(5, q.Poses.Count);
        Assert.True(q.RepeatedImages);
    }

    [Fact]
    public void ClassStepsExpand()
    {
        var q = QueueBuilder.Build(Sess.Ids(20), [], new ClassStepsMode(QueueBuilder.DefaultClass), false, [], Rng);
        Assert.Equal(17, q.Poses.Count);
        Assert.Equal([.. Enumerable.Repeat(30, 10), .. Enumerable.Repeat(60, 5), 300, 300], q.Poses.Select(p => p.Seconds));
        Assert.Equal([.. Enumerable.Repeat<int?>(0, 10), .. Enumerable.Repeat<int?>(1, 5), 2, 2], q.Poses.Select(p => p.StepIndex));
        Assert.False(q.RepeatedImages);
    }

    [Fact]
    public void ZeroCountStepsSkipped()
    {
        var q = QueueBuilder.Build(Sess.Ids(5), [], new ClassStepsMode([new(0, 30), new(2, 60)]), false, [], Rng);
        Assert.Equal([60, 60], q.Poses.Select(p => p.Seconds));
    }

    [Fact]
    public void ShuffleIsPermutation()
    {
        var pool = Sess.Ids(10);
        var q = QueueBuilder.Build(pool, [], new SimpleMode(30, null), true, [], new Random(42));
        Assert.Equal(pool.ToHashSet(), q.Poses.Select(p => p.ImageID).ToHashSet());
        Assert.NotEqual(pool, q.Poses.Select(p => p.ImageID));
    }

    [Fact]
    public void BenchmarksFirstAndNotDuplicated()
    {
        var pool = Sess.Ids(3);
        var q = QueueBuilder.Build(pool, [], new SimpleMode(30, null), false, [new DueBenchmark(pool[0], 120, 3)], Rng);
        Assert.Equal([new Pose(pool[0], 120, 3, null), new Pose(pool[1], 30, null, null), new Pose(pool[2], 30, null, null)], q.Poses);
    }

    [Fact]
    public void EmptyPoolOnlyBenchmarks()
    {
        var withBench = QueueBuilder.Build([], [], new SimpleMode(30, 5), true, [new DueBenchmark(Guid.NewGuid(), 120, 1)], Rng);
        Assert.Single(withBench.Poses);
        Assert.Empty(QueueBuilder.Build([], [], new ClassStepsMode(QueueBuilder.DefaultClass), true, [], Rng).Poses);
    }

    [Fact]
    public void RepeatsNeverBackToBack()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var order = QueueBuilder.Build(Sess.Ids(3), [], new SimpleMode(30, 30), true, [], new Random(seed)).Poses.Select(p => p.ImageID).ToList();
            Assert.All(order.Zip(order.Skip(1)), pair => Assert.NotEqual(pair.First, pair.Second));
        }
    }

    [Fact]
    public void RenderWindowKeepsNeighbours()
    {
        Assert.Equal((0, 1), RenderWindow.Indices(0, 10));
        Assert.Equal((4, 6), RenderWindow.Indices(5, 10));
        Assert.Equal((8, 9), RenderWindow.Indices(9, 10));
    }

    [Fact]
    public void PriorityImagesComeFirstThenFill()
    {
        var today = Sess.Ids(3);
        var rest = Sess.Ids(10);
        var q = QueueBuilder.Build(today, rest, new SimpleMode(60, 8), true, [], new Random(7));
        var order = q.Poses.Select(p => p.ImageID).ToList();
        Assert.Equal(8, order.Count);
        Assert.Equal(today.ToHashSet(), order.Take(3).ToHashSet());
        Assert.Subset(rest.ToHashSet(), order.Skip(3).ToHashSet());
        Assert.Equal(8, order.Distinct().Count());
        Assert.False(q.RepeatedImages);
    }

    [Fact]
    public void FillOnlyUsedWhenNeeded()
    {
        var today = Sess.Ids(5);
        var q = QueueBuilder.Build(today, Sess.Ids(10), new SimpleMode(60, 4), true, [], new Random(7));
        Assert.Subset(today.ToHashSet(), q.Poses.Select(p => p.ImageID).ToHashSet());
    }

    [Fact]
    public void EmptyPriorityStillFills() =>
        Assert.Equal(3, QueueBuilder.Build([], Sess.Ids(4), new SimpleMode(60, 3), true, [], new Random(7)).Poses.Count);
}

public class SessionLengthTests
{
    [Theory]
    [InlineData(1800, 60, 30), InlineData(1800, 120, 15), InlineData(1800, 300, 6), InlineData(1800, 420, 4), InlineData(100, 600, 1)]
    public void PosesForTarget(int target, int pose, int expected) => Assert.Equal(expected, SessionLength.Poses(target, pose));

    [Fact]
    public void ScaledClassAveragesToTarget()
    {
        var scaled = SessionLength.Scaled(QueueBuilder.DefaultClass, 1800);
        Assert.Equal([30, 60, 300], scaled.Select(s => s.Seconds));
        Assert.Equal([15, 8, 3], scaled.Select(s => s.Count));
        Assert.InRange(scaled.Sum(s => s.Count * s.Seconds), 1740, 1860);
    }

    [Fact]
    public void ScaledKeepsEveryStep() =>
        Assert.All(SessionLength.Scaled([new(1, 30), new(2, 600)], 600), s => Assert.True(s.Count >= 1));
}

public class DurationFormatTests
{
    [Theory]
    [InlineData(30, "30s"), InlineData(60, "1 min"), InlineData(90, "1m 30s"), InlineData(300, "5 min")]
    public void Short(int s, string e) => Assert.Equal(e, DurationFormat.Short(s));

    [Theory]
    [InlineData(65.2, "1:06"), InlineData(9, "0:09"), InlineData(0, "0:00")]
    public void Clock(double s, string e) => Assert.Equal(e, DurationFormat.Clock(s));

    [Theory]
    [InlineData(1080, "18 min"), InlineData(3900, "1 h 5 min"), InlineData(20, "20s")]
    public void Total(int s, string e) => Assert.Equal(e, DurationFormat.Total(s));

    [Theory]
    [InlineData("es-MX"), InlineData("ar-SA"), InlineData("de-DE")]
    public void FormattingIgnoresCulture(string culture)
    {
        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            Assert.Equal("1:06", DurationFormat.Clock(65.2));
            Assert.Equal("1 h 5 min", DurationFormat.Total(3900));
            Assert.Equal("0.1", LibraryJson.FormatNumber(0.1));
            Assert.Equal("0.30000000000000004", LibraryJson.FormatNumber(0.30000000000000004));
            Assert.Equal("1", LibraryJson.FormatNumber(1.0));
            Assert.Equal("-1250.5", LibraryJson.FormatNumber(-1250.5));
            Assert.Equal("1e-05", LibraryJson.FormatNumber(0.00001));
            Assert.Equal("2026-11-02", PlanDayKey.Key(new DateOnly(2026, 11, 2)));
            Assert.Equal(new DateOnly(2026, 11, 2), PlanDayKey.Date("2026-11-02"));
            Assert.Equal("w02-mon-focus", P.S().TickKey(new PlanRef(1, 0), PlanBlockKind.Focus));
            Assert.Equal("Week 2 · Monday", P.S().TitleFor(new PlanRef(1, 0)));
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved;
        }
    }
}
