using Linework.Core.Crop;
using Linework.Core.Plan;
using Linework.Core.Session;

namespace Linework.Core.Library;

/// <summary>library.json: marks a folder as a library.</summary>
public sealed record LibraryManifest(int Format, Guid Id, DateTimeOffset CreatedAt)
{
    public const int CurrentFormat = 1;
}

/// <summary>Missing means its file isn't in Photos/ right now. Restoring the file reconnects it.</summary>
public sealed record PhotoRecord(Guid Id, string FileName, string Sha256, DateTimeOffset ImportedAt, NormalizedRect Crop, int Rotation, bool Flipped,
    bool NoFigureFound, bool Missing);

public sealed record BenchmarkRecord(Guid PhotoID, int Seconds, int Interval, DateTimeOffset MarkedAt);

public sealed record AttemptRecord(Guid Id, Guid PhotoID, DateTimeOffset Date, Guid SessionID, string? DrawingFileName = null);

public sealed record BenchmarksFile(IReadOnlyList<BenchmarkRecord> Benchmarks, IReadOnlyList<AttemptRecord> Attempts)
{
    public static readonly BenchmarksFile Empty = new([], []);
}

public sealed record SetRecord(Guid Id, string Name, DateTimeOffset CreatedAt, IReadOnlyList<Guid> PhotoIDs);

public sealed record PresetRecord(Guid Id, string Name, IReadOnlyList<ClassStep> Steps);

public sealed record SessionRecord(Guid Id, DateTimeOffset StartedAt, int DrawingSeconds, int PoseCount, IReadOnlyList<Guid> PhotoIDs);

/// <summary>When this library's Week 1 began (always a Monday), and its repeated weeks.</summary>
public sealed record PlanRecord(string WeekOneStart, IReadOnlyList<PlanRepeat> Repeats);
