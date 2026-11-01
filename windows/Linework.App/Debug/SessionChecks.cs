#if DEBUG
using Linework.App.Library;
using Linework.Core.Library;
using Linework.Core.Plan;
using Linework.Core.Session;

namespace Linework.App.Debugging;

/// <summary>The session checks that need no screen, shared by dotnet test on the Mac and the harness on Windows.</summary>
public static class SessionChecks
{
    public sealed class Effects : ISessionEffects
    {
        public int Held, Ticks, Chimes, Notes;
        public IDisposable KeepAwake()
        {
            Held++;
            return new Done(() => Held--);
        }
        public void Tick() => Ticks++;
        public void Chime() => Chimes++;
        public void Note() => Notes++;
        sealed class Done(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }

    public static void Run(string root, ILibraryHost host, Action<string, bool> check)
    {
        Directory.CreateDirectory(root);
        LibraryFolder.Create(root, "2026-11-02", host.Now);
        var store = new LibraryStore(root, host);
        var photos = Enumerable.Range(0, 3).Select(i => new PhotoRecord(Guid.NewGuid(), $"p{i}.jpg", "h", host.Now, Linework.Core.Crop.CropMath.Unit, 0, false, false, false)).ToList();
        store.AddPhotos(photos, null);
        store.Flush();
        var session = new SessionStore(host);
        var today = host.Today;
        var r = store.Schedule.TodayRef(today);

        (int Ticks, int Sessions) OnDisk()
        {
            var files = new LibraryFiles(root);
            var ticks = files.Read(LibraryFile.Checks) is Loaded<IReadOnlyList<PlanTick>> a ? a.Value.Count : 0;
            var sessions = files.Read(LibraryFile.Sessions) is Loaded<IReadOnlyList<SessionRecord>> b ? b.Value.Count : 0;
            return (ticks, sessions);
        }

        SessionController Start(int seconds, PlanBlockKind kind, Effects effects, bool sound = true)
        {
            var block = new PlanBlock(kind, "check", 5, "Check", ["x"], [new ClassStep(2, seconds)]);
            session.Queue = SessionController.QueueFor(block, photos, today, new Random(1));
            session.PlanBlock = new PlanBlockRef(r, kind);
            session.SoundOn = sound;
            return new SessionController(store, session, host, effects);
        }

        var fx = new Effects();
        var natural = Start(6, PlanBlockKind.Warmup, fx);
        check("A session marks itself active", session.IsSessionActive);
        check("The screen is kept awake during a session", fx.Held == 1);
        natural.Advance(3);
        natural.Advance(6);
        natural.Advance(6);
        check("A session run to its end ticks its lesson on disk", OnDisk().Ticks == 1 && store.IsTicked(r, PlanBlockKind.Warmup));
        check("A session run to its end is recorded on disk", OnDisk().Sessions == 1);
        check("The keep-awake hold is released at the end", fx.Held == 0);
        check("Sound cues play", fx.Chimes > 0 && fx.Ticks > 0);
        natural.Closed();
        check("Closing clears the session", !session.IsSessionActive && session.Queue == null);
        check("Closing doesn't record twice", OnDisk().Sessions == 1);

        var escape = Start(6, PlanBlockKind.Focus, new Effects());
        escape.Advance(3);
        escape.Advance(6);
        escape.End();
        check("Esc records without ticking", OnDisk().Sessions == 2 && !store.IsTicked(r, PlanBlockKind.Focus));
        escape.Closed();

        var skipping = Start(60, PlanBlockKind.Check, new Effects());
        skipping.Advance(1);
        skipping.Next();
        skipping.Next();
        check("Skipping every pose ticks without recording", store.IsTicked(r, PlanBlockKind.Check) && OnDisk().Sessions == 2);
        skipping.Closed();

        var quiet = new Effects();
        var silent = Start(6, PlanBlockKind.BlockA, quiet, false);
        silent.Advance(3);
        silent.Advance(6);
        check("Sound off is silent", quiet.Chimes == 0 && quiet.Ticks == 0);
        silent.End();
        silent.Closed();

        // A benchmark pose stops the clock and asks about the attempt.
        session.Queue = new SessionQueue([new Pose(photos[0].Id, 5, 1, null), new Pose(photos[1].Id, 30, null, null)], false);
        session.PlanBlock = null;
        var benchmark = new SessionController(store, session, host, new Effects());
        PhotoRecord? asked = null;
        benchmark.AttemptNeeded += p => asked = p;
        benchmark.Advance(3);
        benchmark.Advance(5);
        check("A finished benchmark asks about its attempt", asked?.Id == photos[0].Id && benchmark.AttemptFor != null);
        check("The clock waits for the answer", benchmark.Advance(100) == RunnerEvent.None && benchmark.Runner.Index == 0);
        benchmark.AttemptDone();
        check("The session moves on afterwards", benchmark.Runner.Index == 1 && benchmark.AttemptFor == null);
        benchmark.End();
        benchmark.Closed();

        // Notes during a session.
        var notes = new PracticeNotes("Ana", new Random(1));
        notes.Update(0, 0, 10, false, true);
        check("No note in the countdown", notes.Shown == null);
        check("The first-pose note appears when drawing starts", notes.Update(3, 0, 10, true, true) && notes.Shown == "You just began, you can do it.");
        notes.Update(4, 0, 10, false, true);
        check("A note hides while paused", notes.Shown == null);
        notes.Update(5, 0, 10, true, true);
        check("It doesn't come back after the pause", notes.Shown == null);
        check("Encouragement waits five to eight minutes after a note", !notes.Update(200, 0, 10, true, true) && notes.Update(490, 0, 10, true, true) && notes.Shown != null);
        notes.Update(497, 0, 10, true, true);
        check("A note hides after six seconds", notes.Shown == null);
        var off = new PracticeNotes("", new Random(1));
        check("Notes off stays quiet", !off.Update(1, 0, 10, true, false) && off.Shown == null);
    }
}
#endif
