using Linework.App.Debugging;
using Linework.App.Library;
using Linework.Core.Library;

namespace Linework.Core.Tests;

public sealed class FakeHost : ILibraryHost
{
    public List<(TimeSpan Delay, Action Action)> Pending { get; } = [];
    public bool AllowLosing { get; set; }

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
        foreach (var path in paths) if (File.Exists(path)) File.Delete(path);
    }

    public bool ConfirmLosingChanges() => AllowLosing;
    public bool IsReadableImage(string path) => PhotoScan.IsImageName(path) && new FileInfo(path).Length > 0;
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
