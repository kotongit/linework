using Linework.App.Debugging;
using Linework.App.Library;
using Linework.Core.Library;

namespace Linework.Core.Tests;

public sealed class FakeHost : ILibraryHost
{
    public List<(TimeSpan Delay, Action Action)> Pending { get; } = [];
    public bool AllowLosing { get; set; }
    /// <summary>A file the imaging system fails on in a way nobody listed.</summary>
    public string? Poison { get; set; }

    public IDisposable After(TimeSpan delay, Action action)
    {
        var entry = (delay, action);
        Pending.Add(entry);
        return new Cancel(() => Pending.Remove(entry));
    }

    public void RunPending()
    {
        foreach (var (_, action) in Pending.ToList()) action();
    }

    public void MoveToRecycleBin(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    public bool ConfirmLosingChanges() => AllowLosing;
    public bool IsReadableImage(string path) => path == Poison ? throw new FormatException("Bad header") : PhotoScan.IsImageName(path) && File.Exists(path) && new FileInfo(path).Length > 0;
    public (double Width, double Height)? OrientedPixelSize(string path) => IsReadableImage(path) ? (640, 480) : null;
    public (Linework.Core.Crop.NormalizedRect Rect, bool Found) AutoCrop(string path) => (Linework.Core.Crop.CropMath.Unit, false);
    public DateTimeOffset Now => new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    public DateOnly Today => new(2026, 11, 4);

    sealed class Cancel(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

public class StoreTests
{
    [Fact]
    public void StoreChecksPass()
    {
        var failures = new List<string>();
        var count = 0;
        StoreChecks.Run(Path.Combine(Lib.Temp(), "library"), new FakeHost(), (name, ok) => { count++; if (!ok) failures.Add(name); });
        Assert.Empty(failures);
        Assert.True(count > 25);
    }

    [Fact]
    public async Task PhotoChecksPass()
    {
        var failures = new List<string>();
        await PhotoChecks.RunAsync(Lib.Temp(), new FakeHost(), Repo.Fixture("sample-person.jpg"), Repo.Fixture("library/Photos/pose one.png"), (name, ok) => { if (!ok) failures.Add(name); });
        Assert.Empty(failures);
    }

    [Fact]
    public void SessionChecksPass()
    {
        var failures = new List<string>();
        SessionChecks.Run(Path.Combine(Lib.Temp(), "library"), new FakeHost(), (name, ok) => { if (!ok) failures.Add(name); });
        Assert.Empty(failures);
    }

    [Fact]
    public async Task CanvasChecksPass()
    {
        var failures = new List<string>();
        await CanvasChecks.RunAsync(Lib.Temp(), new FakeHost(), Repo.Fixture("sample-person.jpg"), (name, ok) => { if (!ok) failures.Add(name); });
        Assert.Empty(failures);
    }

    static (string Root, FakeHost Host, LibraryStore Store) Fresh()
    {
        var host = new FakeHost();
        var root = Lib.Temp();
        LibraryFolder.Create(root, "2026-11-02", host.Now);
        var store = new LibraryStore(root, host);
        store.Flush();
        return (root, host, store);
    }

    [Fact]
    public async Task AFileThatFailsOddlyIsSkippedNotFatal()
    {
        var (_, host, store) = Fresh();
        var good = Repo.Fixture("sample-person.jpg");
        var bad = Path.Combine(Lib.Temp(), "bad.jpg");
        File.WriteAllBytes(bad, [0xFF, 0xD8, 0xFF]);
        host.Poison = bad;
        var (imported, skipped) = await ImportService.ImportAsync([good, bad], store, host, null);
        Assert.Equal((1, 1), (imported, skipped));
        Assert.Equal(0, store.ImportsRunning);
    }

    [Fact]
    public async Task AFileThatFailsOddlyDoesNotStopTheScan()
    {
        var (_, host, store) = Fresh();
        var bad = Path.Combine(store.PhotosDir, "bad.jpg");
        File.WriteAllBytes(bad, [0xFF, 0xD8, 0xFF]);
        File.Copy(Repo.Fixture("sample-person.jpg"), Path.Combine(store.PhotosDir, "good.jpg"));
        host.Poison = bad;
        await PhotoScanner.RunAsync(store, host);
        Assert.Equal("good.jpg", Assert.Single(store.Photos).FileName);
    }

    [Fact]
    public void AFileFromANewerAppIsNotSavedOver()
    {
        var (_, _, store) = Fresh();
        var path = store.Files.PathOf(LibraryFile.Sets);
        store.CreateSet("Mine");
        store.Flush();
        const string newer = """{"version":99,"items":[]}""";
        File.WriteAllText(path, newer);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
        store.ReloadIfChanged();
        store.CreateSet("Another");
        Assert.False(store.Flush());
        Assert.Equal(newer, File.ReadAllText(path));
        Assert.NotNull(store.SaveFailure);
    }

    [Fact]
    public void AReloadThatFailsIsTriedAgain()
    {
        if (OperatingSystem.IsWindows()) return; // No POSIX modes there.
        var (_, _, store) = Fresh();
        store.CreateSet("Old");
        store.Flush();
        var path = store.Files.PathOf(LibraryFile.Sets);
        var text = File.ReadAllText(path).Replace("Old", "From the Mac");
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
        File.SetUnixFileMode(path, UnixFileMode.None);
        store.ReloadIfChanged();
        Assert.Equal("Old", store.Sets[0].Name);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        store.ReloadIfChanged();
        Assert.Equal("From the Mac", store.Sets[0].Name);
    }

    [Fact]
    public void ASaveThatWorksDoesNotHideOneThatFailed()
    {
        var (_, host, store) = Fresh();
        // A folder where the file goes makes that one save fail, and only that one.
        Directory.CreateDirectory(store.Files.PathOf(LibraryFile.Photos));
        store.AddPhotos([Lib.Photo()], null);
        store.CreateSet("Saves fine");
        Assert.False(store.Flush());
        Assert.NotNull(store.SaveFailure);
        Assert.Single(Lib.Loaded(store.Files.Read(LibraryFile.Sets)));
        Assert.NotEmpty(host.Pending); // It tries again by itself.
    }

    [Fact]
    public async Task RecordsSetAsideEarlierStillPauseTheScan()
    {
        var (root, host, store) = Fresh();
        await ImportService.ImportAsync([Repo.Fixture("sample-person.jpg")], store, host, null);
        store.Flush();
        File.WriteAllText(store.Files.PathOf(LibraryFile.Photos), "junk");
        _ = new LibraryStore(root, host); // Sets the file aside.
        var relaunched = new LibraryStore(root, host);
        await PhotoScanner.RunAsync(relaunched, host);
        Assert.Empty(relaunched.Photos);
        Assert.NotNull(relaunched.Notice);
    }

    /// <summary>The bar reads Status every frame while panning, so it can't go to the disk.</summary>
    [Fact]
    public void StatusReadsNoFolderUntilTheListRefreshes()
    {
        if (OperatingSystem.IsWindows()) return; // Windows can't make such a name.
        var root = Lib.Temp();
        var directory = new Linework.Core.Freehand.CanvasDirectory(root);
        var store = new FreehandStore(directory, Guid.NewGuid(), new FakeHost(), new Settings(null));
        store.LoadIfNeeded();
        Directory.CreateDirectory(Path.Combine(root, "a?b"));
        Assert.Null(store.Status);
        store.RefreshList();
        Assert.Contains("a?b", store.Status);
    }

    [Fact]
    public void ChangesSaveAfterADelayAndAtFlush()
    {
        var host = new FakeHost();
        var root = Lib.Temp();
        LibraryFolder.Create(root, "2026-11-02", host.Now);
        var store = new LibraryStore(root, host);
        store.Flush();
        store.CreateSet("Later");
        Assert.Empty(Lib.Loaded(store.Files.Read(LibraryFile.Sets) is Missing<IReadOnlyList<SetRecord>> ? new Loaded<IReadOnlyList<SetRecord>>([]) : store.Files.Read(LibraryFile.Sets)));
        Assert.Contains(host.Pending, p => p.Delay == TimeSpan.FromMilliseconds(500));
        host.RunPending();
        Assert.Single(Lib.Loaded(store.Files.Read(LibraryFile.Sets)));
    }

    [Fact]
    public void ANoticeClearsItself()
    {
        var host = new FakeHost();
        var root = Lib.Temp();
        LibraryFolder.Create(root, "2026-11-02", host.Now);
        var store = new LibraryStore(root, host) { Notice = "Hello" };
        store.Flush();
        host.RunPending();
        Assert.Null(store.Notice);
    }

    [Fact]
    public void AFixtureLibraryOpensAndSavesUnchanged()
    {
        var root = Lib.Temp();
        CopyTree(Repo.Fixture("library"), root);
        var store = new LibraryStore(root, new FakeHost());
        Assert.Equal(3, store.Photos.Count);
        Assert.Equal(2, store.Sets.Count);
        Assert.True(store.TickKeys.Contains("w02-mon-focus-r1"));
        store.CreateSet("x");
        store.DeleteSet(store.Sets[^1].Id);
        store.Flush();
        Assert.Equal(File.ReadAllBytes(Repo.Fixture("library/Progress/sets.json")), File.ReadAllBytes(Path.Combine(root, "Progress/sets.json")));
    }

    internal static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
