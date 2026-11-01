namespace Linework.Core.Session;

public enum RunnerPhase { GetReady, Pose, AwaitingAttempt, Finished }

[Flags]
public enum RunnerEvent
{
    None = 0,
    /// <summary>A whole second passed during the last 5 seconds of a pose.</summary>
    Tick = 1 << 0,
    PoseChanged = 1 << 1,
    StepChanged = 1 << 2,
    BenchmarkEnded = 1 << 3,
    Finished = 1 << 4,
    /// <summary>A countdown ended and the pose's time is running.</summary>
    DrawingStarted = 1 << 5,
}

/// <summary>Timer state machine for a session; the view drives it with real elapsed time.</summary>
public sealed class SessionRunner
{
    public const int TickWindow = 5;

    public IReadOnlyList<Pose> Poses { get; }
    public double GetReadySeconds { get; }
    public int Index { get; private set; }
    public RunnerPhase Phase { get; private set; }
    /// <summary>Time left of the countdown, while Phase is GetReady.</summary>
    public double GetReadyRemaining { get; private set; }
    public double Remaining { get; private set; }
    public bool IsPaused { get; private set; }
    public double DrawingSeconds { get; private set; }
    readonly double[] _poseElapsed;
    /// <summary>← never goes below this; set past a benchmark once its attempt is recorded.</summary>
    int _minIndex;
    bool _endedEarly;

    public SessionRunner(IReadOnlyList<Pose> poses, double getReadySeconds = 3)
    {
        Poses = poses;
        GetReadySeconds = getReadySeconds;
        Phase = poses.Count == 0 ? RunnerPhase.Finished : RunnerPhase.GetReady;
        GetReadyRemaining = getReadySeconds;
        Remaining = poses.Count > 0 ? poses[0].Seconds : 0;
        _poseElapsed = new double[poses.Count];
    }

    public IReadOnlyList<double> PoseElapsed => _poseElapsed;

    public Pose? Current => Index >= 0 && Index < Poses.Count ? Poses[Index] : null;

    public bool ShouldRecord => _poseElapsed.Any(e => e >= 5);

    public RunnerEvent Tick(double dt)
    {
        if (IsPaused) return RunnerEvent.None;
        switch (Phase)
        {
            case RunnerPhase.GetReady:
                var left = GetReadyRemaining - dt;
                if (left <= 0)
                {
                    Phase = RunnerPhase.Pose;
                    Remaining = Poses[Index].Seconds;
                    return RunnerEvent.DrawingStarted;
                }
                GetReadyRemaining = left;
                return RunnerEvent.None;
            case RunnerPhase.Pose:
                var before = Remaining;
                var spent = Math.Min(dt, before);
                Remaining = before - dt;
                DrawingSeconds += spent;
                _poseElapsed[Index] += spent;
                var events = RunnerEvent.None;
                for (var s = 1; s <= TickWindow; s++)
                    if (s < before && s >= Remaining) { events |= RunnerEvent.Tick; break; }
                if (Remaining <= 0) events |= Advance();
                return events;
            default:
                return RunnerEvent.None;
        }
    }

    public void TogglePause() => IsPaused = !IsPaused;

    public RunnerEvent Next() => Phase is RunnerPhase.Pose or RunnerPhase.GetReady ? Advance() : RunnerEvent.None;

    /// <summary>Goes back one pose with its full time; at the first pose (or just after a handled benchmark), restarts it.</summary>
    public RunnerEvent Previous()
    {
        if (Phase == RunnerPhase.GetReady && Index <= _minIndex) return RunnerEvent.None;
        if (Phase is not (RunnerPhase.Pose or RunnerPhase.GetReady)) return RunnerEvent.None;
        var moved = Index > _minIndex;
        if (moved) Index--;
        Remaining = Poses[Index].Seconds;
        Phase = RunnerPhase.Pose;
        return moved ? RunnerEvent.PoseChanged : RunnerEvent.None;
    }

    public RunnerEvent AttemptHandled()
    {
        if (Phase != RunnerPhase.AwaitingAttempt) return RunnerEvent.None;
        _minIndex = Index + 1;
        return Move(Index + 1);
    }

    public void End()
    {
        if (Phase != RunnerPhase.Finished) _endedEarly = true;
        Phase = RunnerPhase.Finished;
    }

    /// <summary>The session ran to its end, rather than being ended with Esc.</summary>
    public bool CompletedNaturally => Phase == RunnerPhase.Finished && Poses.Count > 0 && !_endedEarly;

    RunnerEvent Advance()
    {
        if (Poses[Index].BenchmarkNumber != null)
        {
            Phase = RunnerPhase.AwaitingAttempt;
            return RunnerEvent.BenchmarkEnded;
        }
        return Move(Index + 1);
    }

    RunnerEvent Move(int i)
    {
        if (i >= Poses.Count)
        {
            Phase = RunnerPhase.Finished;
            return RunnerEvent.Finished;
        }
        var previousStep = Poses[Index].StepIndex;
        Index = i;
        Remaining = Poses[i].Seconds;
        var events = RunnerEvent.PoseChanged;
        if (Poses[i].StepIndex is { } step && step != previousStep)
        {
            events |= RunnerEvent.StepChanged;
            Phase = RunnerPhase.GetReady;
            GetReadyRemaining = GetReadySeconds;
        }
        else
        {
            Phase = RunnerPhase.Pose;
        }
        return events;
    }
}

/// <summary>Which pose images the player keeps rendered: the previous, current and next.</summary>
public static class RenderWindow
{
    public static (int Lower, int Upper) Indices(int index, int count) => (Math.Max(index - 1, 0), Math.Min(index + 1, Math.Max(count - 1, 0)));
}
