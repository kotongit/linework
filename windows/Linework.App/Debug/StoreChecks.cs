#if DEBUG
using Linework.App.Library;
using Linework.Core.Library;
using Linework.Core.Plan;

namespace Linework.App.Debugging;

/// <summary>The store checks, shared: the Mac runs them through dotnet test, Windows through the harness.</summary>
public static class StoreChecks
{
    public static void Run(string root, ILibraryHost host, Action<string, bool> check)
    {
        var settings = new Settings(null);
        var app = new AppModel(host, settings);
        app.Create(root);
        check("A scratch library opens", app.Library != null);
        foreach (var name in LibraryFolder.Folders.Append(LibraryFolder.ManifestName))
            check($"The library has {name}", Directory.Exists(Path.Combine(root, name)) || File.Exists(Path.Combine(root, name)));
        var store = app.Library!;
        store.Flush();
        check("A new library has one preset", store.Presets.Count == 1);
        var r = store.Schedule.TodayRef(host.Today);
        check("A tick saves at once", store.ToggleTick(r, PlanBlockKind.Focus) && File.Exists(store.Files.PathOf(LibraryFile.Checks)));
        check("A reopened library has the tick", new LibraryStore(root, host).IsTicked(r, PlanBlockKind.Focus));

        // The folder goes away while the app is open.
        var away = root + ".away";
        Directory.Move(root, away);
        var kept = store.ToggleTick(r, PlanBlockKind.Check);
        check("A missing library reports the failed save", store.SaveFailure != null);
        check("A missing library isn't recreated", !Directory.Exists(root));
        check("The edit stays in memory", kept && store.IsTicked(r, PlanBlockKind.Check));
        check("Asking to leave asks first", !app.SaveOrConfirmLosing() || host.ConfirmLosingChanges());
        Directory.Move(away, root);
        store.Flush();
        check("The save succeeds once the folder is back", store.SaveFailure == null && new LibraryStore(root, host).IsTicked(r, PlanBlockKind.Check));
        store.ToggleTick(r, PlanBlockKind.Focus);
        store.ToggleTick(r, PlanBlockKind.Check);

        var bas = Path.GetDirectoryName(root)!;
        LibraryStore Fresh(string name)
        {
            var path = Path.Combine(bas, $"{name}-{Guid.NewGuid()}");
            Directory.CreateDirectory(path);
            LibraryFolder.Create(path, "2026-11-02", host.Now);
            return new LibraryStore(path, host);
        }

        // A file that's there but can't be read must stop the library opening, not empty it.
        var a = Fresh("unreadable");
        var ar = a.Schedule.TodayRef(host.Today);
        a.ToggleTick(ar, PlanBlockKind.Focus);
        a.Flush();
        var ticks = a.Files.PathOf(LibraryFile.Checks);
        var bytes = File.ReadAllBytes(ticks);
        using (Lock(ticks))
        {
            var stopped = false;
            try { _ = new LibraryStore(a.Root, host); } catch (UnreadableException) { stopped = true; }
            check("An unreadable file stops the library opening", stopped);
            var openFailed = new AppModel(host, new Settings(null));
            openFailed.Open(a.Root);
            check("The chooser says why", openFailed.Library == null && openFailed.Message is ChooserFailed);
            a.ReloadIfChanged();
            check("Reloading keeps what's in memory", a.IsTicked(ar, PlanBlockKind.Focus));
        }
        check("The unreadable file is untouched", File.ReadAllBytes(ticks).SequenceEqual(bytes));

        // A file damaged while open must not empty memory, and memory saves back.
        File.WriteAllText(ticks, "junk");
        File.SetLastWriteTimeUtc(ticks, DateTime.UtcNow.AddSeconds(5));
        a.ReloadIfChanged();
        check("A file damaged while open doesn't empty memory", a.IsTicked(ar, PlanBlockKind.Focus));
        check("It's reported", a.DamagedFiles.Contains("checks.json"));
        a.Flush();
        check("Memory saves back in its place", new LibraryStore(a.Root, host).IsTicked(ar, PlanBlockKind.Focus));
        check("The damaged copy is kept", File.ReadAllText(Path.Combine(a.Root, "Progress", "checks.damaged.json")) == "junk");

        // A missing plan file is saved, so Week 1 can't drift.
        var e = Fresh("plan");
        File.Delete(e.Files.PathOf(LibraryFile.Plan));
        var replanned = new LibraryStore(e.Root, host);
        replanned.Flush();
        check("A missing plan is written back", File.Exists(e.Files.PathOf(LibraryFile.Plan)));

        // A newer library isn't opened or touched.
        var n = Fresh("newer");
        File.WriteAllText(n.Files.PathOf(LibraryFile.Checks), """{"version":99,"items":[]}""");
        var refused = new AppModel(host, new Settings(null));
        refused.Open(n.Root);
        check("A file from a newer app stops the library opening", refused.Library == null && refused.Message is ChooserNewerFormat);

        // A library that's gone is reported, never recreated.
        var gone = Path.Combine(bas, $"gone-{Guid.NewGuid()}");
        var missing = new AppModel(host, new Settings(null));
        missing.Open(gone);
        check("A missing folder is reported", missing.Message is ChooserMissing);
        check("A missing folder isn't recreated", !Directory.Exists(gone));

        // A folder with other things is asked about first.
        var other = Path.Combine(bas, $"other-{Guid.NewGuid()}");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "mine.txt"), "mine");
        var asking = new AppModel(host, new Settings(null));
        asking.Open(other);
        check("A folder with other files is asked about", asking.PendingAdoption == other && asking.Library == null);

        // Today moves on.
        check("The schedule uses the library's start, not today", e.Schedule.DateOf(new PlanRef(0, 0)) == new DateOnly(2026, 11, 2));

        // Ticking.
        var t = Fresh("ticking");
        var start = t.Schedule.TodayRef(host.Today);
        var current = start is PlanRef p ? p.SequenceIndex : 0;
        var ahead = new PlanRef(current + 4, 0);
        var kind = t.Schedule.DayFor(ahead).Blocks[0].Kind;
        t.ToggleTick(ahead, kind);
        var tick = t.Ticks.FirstOrDefault(x => x.Key == t.Schedule.TickKey(ahead, kind));
        check("A future lesson can be ticked", t.IsTicked(ahead, kind));
        check("It counts on the earlier of its day and today", tick?.CountsOn == PlanTick.CountsOnFor(t.Schedule.DateOf(ahead), host.Today));
        check("It shows on the Path", PlanProgress.Status(ahead, t.TickKeys, t.Schedule) != DayStatus.Empty);

        // Repeats. They need today to be inside the plan.
        var rp = Path.Combine(bas, $"repeats-{Guid.NewGuid()}");
        Directory.CreateDirectory(rp);
        LibraryFolder.Create(rp, LibraryFolder.MondayOf(host.Today), host.Now);
        var s = new LibraryStore(rp, host);
        var now = (PlanRef)s.Schedule.TodayRef(host.Today);
        var later = new PlanRef(now.SequenceIndex + 4, 0);
        var laterKind = s.Schedule.DayFor(later).Blocks[0].Kind;
        s.ToggleTick(later, laterKind);
        check("The streak includes today", PlanProgress.CurrentStreak(s.PracticeDays, host.Today) >= 1);
        s.AddRepeat(1, host.Today);
        var shifted = new PlanRef(now.SequenceIndex + 5, 0);
        check("A repeat before it leaves the tick on its lesson", s.IsTicked(shifted, laterKind));
        var copy = new PlanRef(now.SequenceIndex + 1, 0);
        s.ToggleTick(copy, s.Schedule.DayFor(copy).Blocks[0].Kind);
        var before = s.Ticks.Count;
        s.UndoLastRepeat();
        check("Undo removes only that repeat's ticks", s.Ticks.Count == before - 1 && s.IsTicked(later, laterKind));
        check("The repeat is gone from disk", new LibraryStore(rp, host).Plan.Repeats.Count == 0);
    }

    /// <summary>Makes a file unreadable: a lock on Windows, its mode elsewhere.</summary>
    public static IDisposable Lock(string path)
    {
        if (OperatingSystem.IsWindows()) return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, UnixFileMode.None);
        return new Restore(() => File.SetUnixFileMode(path, mode));
    }

    sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
#endif
