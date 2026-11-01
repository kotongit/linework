namespace Linework.Core.Session;

public sealed record ClassStep(int Count, int Seconds);

public abstract record SessionMode;
public sealed record SimpleMode(int Seconds, int? MaxPoses) : SessionMode;
public sealed record ClassStepsMode(IReadOnlyList<ClassStep> Steps) : SessionMode;

/// <summary>Number is the attempt number: existing attempts + 1.</summary>
public sealed record DueBenchmark(Guid ImageID, int Seconds, int Number);

/// <summary>StepIndex is the class step; null in simple mode and for benchmarks.</summary>
public sealed record Pose(Guid ImageID, int Seconds, int? BenchmarkNumber, int? StepIndex);

public sealed record SessionQueue(IReadOnlyList<Pose> Poses, bool RepeatedImages);

public static class QueueBuilder
{
    public static readonly IReadOnlyList<ClassStep> DefaultClass = [new(10, 30), new(5, 60), new(2, 300)];

    static List<Guid> Shuffled(IEnumerable<Guid> ids, Random rng)
    {
        var list = ids.ToArray();
        rng.Shuffle(list);
        return [.. list];
    }

    /// <summary>imageIDs play first; fillIDs (e.g. the rest of the library) fill any remaining poses.</summary>
    public static SessionQueue Build(IReadOnlyList<Guid> imageIDs, IReadOnlyList<Guid> fillIDs, SessionMode mode, bool shuffle,
        IReadOnlyList<DueBenchmark> benchmarks, Random rng)
    {
        var benchmarkIDs = benchmarks.Select(b => b.ImageID).ToHashSet();
        var priority = imageIDs.Where(id => !benchmarkIDs.Contains(id)).ToList();
        var priorityIDs = priority.ToHashSet();
        var fill = fillIDs.Where(id => !benchmarkIDs.Contains(id) && !priorityIDs.Contains(id)).ToList();
        var pool = priority.Concat(fill).ToList();
        var poses = benchmarks.Select(b => new Pose(b.ImageID, b.Seconds, b.Number, null)).ToList();

        // (seconds, stepIndex) for each regular pose.
        var slots = mode switch
        {
            SimpleMode s => Enumerable.Repeat((s.Seconds, (int?)null), s.MaxPoses ?? pool.Count).ToList(),
            ClassStepsMode c => c.Steps.SelectMany((step, i) => Enumerable.Repeat((step.Seconds, (int?)i), Math.Max(step.Count, 0))).ToList(),
            _ => [],
        };

        if (pool.Count == 0) return new SessionQueue(poses, false);

        // First pass: priority images, then fillers; later passes mix everything.
        var order = shuffle ? Shuffled(priority, rng).Concat(Shuffled(fill, rng)).ToList() : [.. pool];
        while (order.Count < slots.Count)
        {
            var pass = shuffle ? Shuffled(pool, rng) : [.. pool];
            // Don't show the same image twice in a row across passes.
            if (pass.Count > 1 && pass[0] == order[^1]) (pass[0], pass[^1]) = (pass[^1], pass[0]);
            order.AddRange(pass);
        }
        foreach (var (slot, id) in slots.Zip(order))
            poses.Add(new Pose(id, slot.Item1, null, slot.Item2));
        return new SessionQueue(poses, slots.Count > pool.Count);
    }
}

/// <summary>Sizes a session to a target length.</summary>
public static class SessionLength
{
    public const int DefaultTargetSeconds = 30 * 60;

    /// <summary>Pose count whose total is closest to the target (at least one).</summary>
    public static int Poses(int targetSeconds, int poseSeconds) =>
        Math.Max((int)Math.Round((double)targetSeconds / Math.Max(poseSeconds, 1), MidpointRounding.AwayFromZero), 1);

    /// <summary>Scales each step's count so the class totals about the target, keeping every step.</summary>
    public static IReadOnlyList<ClassStep> Scaled(IReadOnlyList<ClassStep> steps, int toSeconds)
    {
        var total = steps.Sum(s => Math.Max(s.Count, 0) * s.Seconds);
        if (total <= 0) return steps;
        var factor = (double)toSeconds / total;
        return steps.Select(s => new ClassStep(Math.Max((int)Math.Round(s.Count * factor, MidpointRounding.AwayFromZero), 1), s.Seconds)).ToList();
    }
}
