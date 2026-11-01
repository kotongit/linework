using Linework.Core.Copy;
using Linework.Core.Library;
using Linework.Core.Plan;
using Linework.Core.Session;

namespace Linework.App.Library;

/// <summary>Sounds and keeping the screen on, for a session. Windows gives the real thing; tests count the calls.</summary>
public interface ISessionEffects
{
    IDisposable KeepAwake();
    void Tick();
    void Chime();
    void Note();
}

/// <summary>What the player window drives. No WPF here, so recording and ticking are checked on the Mac too.</summary>
public sealed class SessionController
{
    readonly LibraryStore _library;
    readonly SessionStore _store;
    readonly ILibraryHost _host;
    readonly ISessionEffects _effects;
    readonly PlanBlockRef? _activeBlock;
    readonly DateTimeOffset _startedAt;
    IDisposable? _keepAwake;
    bool _saved;

    public SessionRunner Runner { get; }
    public Guid SessionId { get; } = Guid.NewGuid();
    public bool SoundOn { get; }
    public bool Recorded { get; private set; }
    public PlanBlockKind? TickedKind { get; private set; }
    /// <summary>The session's images, taken once at start, so a change in the library can't disturb a running session.</summary>
    public IReadOnlyDictionary<Guid, PhotoRecord> ImagesById { get; }
    /// <summary>A benchmark pose ended and this photo's attempt is being asked about. The clock waits.</summary>
    public PhotoRecord? AttemptFor { get; private set; }
    public event Action<PhotoRecord>? AttemptNeeded;

    public SessionController(LibraryStore library, SessionStore store, ILibraryHost host, ISessionEffects effects)
    {
        _library = library;
        _store = store;
        _host = host;
        _effects = effects;
        var queue = store.Queue ?? new SessionQueue([], false);
        _activeBlock = store.PlanBlock;
        SoundOn = store.SoundOn;
        _startedAt = host.Now;
        var ids = queue.Poses.Select(p => p.ImageID).ToHashSet();
        ImagesById = library.Photos.Where(p => ids.Contains(p.Id)).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        Runner = new SessionRunner(queue.Poses);
        store.SetSessionActive(true);
        _keepAwake = effects.KeepAwake();
    }

    public bool IsDrawing => Runner.Phase == RunnerPhase.Pose && !Runner.IsPaused && AttemptFor == null;

    /// <summary>Real elapsed seconds since the last call.</summary>
    public RunnerEvent Advance(double dt)
    {
        if (AttemptFor != null) return RunnerEvent.None;
        return Handle(Runner.Tick(dt));
    }

    public RunnerEvent Next() => Handle(Runner.Next());
    public RunnerEvent Previous() => Handle(Runner.Previous());

    public void AttemptDone()
    {
        AttemptFor = null;
        Handle(Runner.AttemptHandled());
    }

    /// <summary>Esc: ends the session early, or leaves the summary once it's over.</summary>
    public void End()
    {
        Runner.End();
        Finish();
    }

    RunnerEvent Handle(RunnerEvent events)
    {
        if (events.HasFlag(RunnerEvent.Tick) && SoundOn) _effects.Tick();
        if (events.HasFlag(RunnerEvent.DrawingStarted) && SoundOn) _effects.Chime();
        if (events.HasFlag(RunnerEvent.BenchmarkEnded))
        {
            if (SoundOn) _effects.Chime();
            if (Runner.Current is { } pose && ImagesById.TryGetValue(pose.ImageID, out var photo))
            {
                AttemptFor = photo;
                AttemptNeeded?.Invoke(photo);
            }
            else
            {
                Handle(Runner.AttemptHandled());
            }
            return events;
        }
        if (events.HasFlag(RunnerEvent.PoseChanged) && SoundOn) _effects.Chime();
        if (events.HasFlag(RunnerEvent.Finished)) Finish();
        return events;
    }

    /// <summary>A plan session that ran to its end ticks its block. Ending with Esc doesn't.</summary>
    void TickPlanBlock()
    {
        if (!Runner.CompletedNaturally || _activeBlock == null) return;
        TickedKind = _activeBlock.Kind;
        if (_library.EnsureTick(_activeBlock.Ref, _activeBlock.Kind)
            && PlanProgress.CelebrationFor(_activeBlock.Ref, _library.TickKeys, _library.Schedule) is { } celebration)
            _store.Celebrate(celebration, _activeBlock.Ref == _library.Schedule.TodayRef(_store.Today));
    }

    /// <summary>Records the session once, if any pose ran long enough.</summary>
    public void Finish()
    {
        _keepAwake?.Dispose();
        _keepAwake = null;
        if (_saved) return;
        _saved = true;
        TickPlanBlock();
        if (!Runner.ShouldRecord) return;
        var shown = Runner.Poses.Zip(Runner.PoseElapsed).Where(p => p.Second > 0).Select(p => p.First.ImageID).ToList();
        _library.RecordSession(new SessionRecord(SessionId, _startedAt, (int)Runner.DrawingSeconds, Runner.PoseElapsed.Count(e => e >= 5), shown));
        Recorded = true;
    }

    /// <summary>The window has gone.</summary>
    public void Closed()
    {
        Finish();
        _store.Queue = null;
        _store.SetSessionActive(false);
    }

    /// <summary>A class session of the block's steps: today's imports first, then random library photos. No benchmarks.</summary>
    public static SessionQueue? QueueFor(PlanBlock block, IReadOnlyList<PhotoRecord> photos, DateOnly today, Random rng)
    {
        if (block.Timed == null || photos.Count == 0) return null;
        bool IsToday(PhotoRecord p) => DateOnly.FromDateTime(p.ImportedAt.ToLocalTime().DateTime) == today;
        return QueueBuilder.Build(photos.Where(IsToday).Select(p => p.Id).ToList(), photos.Where(p => !IsToday(p)).Select(p => p.Id).ToList(),
            new ClassStepsMode(block.Timed), true, [], rng);
    }
}

/// <summary>The notes during a timed session: milestones as poses are reached, encouragement in the long stretches between.
/// Quiet during countdowns and pauses. One per session, so the milestones start over.</summary>
public sealed class PracticeNotes(string name, Random rng)
{
    readonly PracticeNoteState _notes = new();
    double? _hideAt, _nextLineAt;
    (int Pose, bool Drawing, bool Enabled)? _moment;

    public string? Shown { get; private set; }

    /// <summary>now is seconds on a clock that doesn't count sleep. True if a new note just appeared.</summary>
    public bool Update(double now, int poseIndex, int poseCount, bool isDrawing, bool enabled)
    {
        var appeared = false;
        void Show(string line)
        {
            Shown = line;
            appeared = true;
            _hideAt = now + SeaNotes.PracticeVisibleSeconds;
            _nextLineAt = now + SeaNotes.NextPracticeInterval(rng);
        }
        var moment = (poseIndex, isDrawing, enabled);
        if (moment != _moment)
        {
            _moment = moment;
            if (!enabled || !isDrawing)
            {
                Shown = null;
                _hideAt = null;
                return false;
            }
            _nextLineAt ??= now + SeaNotes.PracticeFirstDelay;
            if (SeaNotes.PracticeMilestone(poseIndex, poseCount, _notes) is { } milestone) Show(milestone);
        }
        if (!enabled || !isDrawing) return false;
        if (_hideAt is { } hide && now >= hide)
        {
            Shown = null;
            _hideAt = null;
        }
        if (_nextLineAt is { } next && now >= next) Show(SeaNotes.PracticeLine(name, _notes, rng));
        return appeared;
    }
}
