using System.Text;
using Linework.Core.Crop;
using Linework.Core.Freehand;
using Linework.Core.Library;
using Linework.Core.Plan;
using Linework.Core.Session;

namespace Linework.Core.Tests;

static class Lib
{
    public static readonly DateTimeOffset Date = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    public static string Temp(bool progress = false)
    {
        var path = Path.Combine(Path.GetTempPath(), "lw-" + Guid.NewGuid());
        Directory.CreateDirectory(progress ? Path.Combine(path, "Progress") : path);
        return path;
    }
    public static PhotoRecord Photo(string name = "pose.jpg", string hash = "abc", bool missing = false) =>
        new(Guid.NewGuid(), name, hash, Date, new NormalizedRect(0.1, 0.2, 0.5, 0.6), 90, true, false, missing);
    public static T Loaded<T>(LoadResult<T> r) => Assert.IsType<Loaded<T>>(r).Value;
}

public class LibraryFilesTests
{
    [Fact]
    public void RoundTripsEveryFile()
    {
        var files = new LibraryFiles(Lib.Temp(true));
        var id = Guid.NewGuid();
        var attempt = new AttemptRecord(Guid.NewGuid(), id, Lib.Date, Guid.NewGuid(), "a.jpg");
        var photo = Lib.Photo();
        files.Write(LibraryFile.Photos, [photo]);
        files.Write(LibraryFile.Benchmarks, new BenchmarksFile([new BenchmarkRecord(id, 120, 10, Lib.Date)], [attempt]));
        files.Write(LibraryFile.Sets, [new SetRecord(id, "Figures", Lib.Date, [id])]);
        files.Write(LibraryFile.Presets, [new PresetRecord(id, "Default class", [new ClassStep(2, 30)])]);
        files.Write(LibraryFile.Checks, [new PlanTick("w01-mon-focus", "2026-11-02", Lib.Date)]);
        files.Write(LibraryFile.Plan, new PlanRecord("2026-11-02", [new PlanRepeat(1, 2, 1)]));
        files.Write(LibraryFile.Sessions, [new SessionRecord(id, Lib.Date, 60, 2, [id])]);
        Assert.Equal(photo, Lib.Loaded(files.Read(LibraryFile.Photos))[0]);
        Assert.Equal(attempt, Lib.Loaded(files.Read(LibraryFile.Benchmarks)).Attempts[0]);
        Assert.Equal([id], Lib.Loaded(files.Read(LibraryFile.Sets))[0].PhotoIDs);
        Assert.Equal(new ClassStep(2, 30), Lib.Loaded(files.Read(LibraryFile.Presets))[0].Steps[0]);
        Assert.Equal(new PlanTick("w01-mon-focus", "2026-11-02", Lib.Date), Lib.Loaded(files.Read(LibraryFile.Checks))[0]);
        Assert.Equal(new PlanRepeat(1, 2, 1), Lib.Loaded(files.Read(LibraryFile.Plan)).Repeats[0]);
        Assert.Equal(60, Lib.Loaded(files.Read(LibraryFile.Sessions))[0].DrawingSeconds);
    }

    [Fact]
    public void MissingFileIsMissing() => Assert.IsType<Missing<IReadOnlyList<PhotoRecord>>>(new LibraryFiles(Lib.Temp(true)).Read(LibraryFile.Photos));

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"version":1,"items":null}""")]
    [InlineData("""{"version":1,"items":[null]}""")]
    [InlineData("""{"version":1,"items":[{"key":"k","checkedAt":"2026-11-02T12:00:00Z"}]}""")]
    [InlineData("""{"version":1,"items":[{"key":null,"countsOn":"d","checkedAt":"2026-11-02T12:00:00Z"}]}""")]
    [InlineData("""{"version":1,"items":[{"key":"k","countsOn":"d","checkedAt":"2026-11-02T12:00:00.5Z"}]}""")]
    public void DamagedFileIsSetAsideNotOverwritten(string text)
    {
        var root = Lib.Temp(true);
        var url = Path.Combine(root, "Progress/checks.json");
        File.WriteAllText(url, text);
        var result = new LibraryFiles(root).Read(LibraryFile.Checks);
        var aside = Path.Combine(root, "Progress", "checks.damaged.json");
        Assert.Equal(aside, Assert.IsType<Damaged<IReadOnlyList<PlanTick>>>(result).SetAsideAs);
        Assert.Equal(text, File.ReadAllText(aside));
        Assert.False(File.Exists(url));
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        var root = Lib.Temp(true);
        File.WriteAllText(Path.Combine(root, "Progress/checks.json"), """{"version":1,"later":true,"items":[{"key":"k","countsOn":"d","checkedAt":"2026-11-02T12:00:00Z","x":[1]}]}""");
        Assert.Equal("k", Lib.Loaded(new LibraryFiles(root).Read(LibraryFile.Checks))[0].Key);
    }

    [Fact]
    public void NewerVersionThrowsAndChangesNothing()
    {
        var root = Lib.Temp(true);
        var url = Path.Combine(root, "Progress/checks.json");
        const string bytes = """{"version":99,"items":[]}""";
        File.WriteAllText(url, bytes);
        Assert.Equal("checks.json", Assert.Throws<NewerFormatException>(() => new LibraryFiles(root).Read(LibraryFile.Checks)).File);
        Assert.Equal(bytes, File.ReadAllText(url));
    }

    [Fact]
    public void WritesAreIndentedSortedISO()
    {
        var root = Lib.Temp(true);
        new LibraryFiles(root).Write(LibraryFile.Checks, [new PlanTick("a/b", "2026-11-02", Lib.Date)]);
        var text = File.ReadAllText(Path.Combine(root, "Progress/checks.json"));
        Assert.Contains("\"version\" : 1", text);
        Assert.Contains("\n  ", text);
        Assert.Contains("\"a/b\"", text);
        Assert.Contains("2026-11-02T12:00:00Z", text);
        Assert.True(text.IndexOf("\"items\"") < text.IndexOf("\"version\""));
    }

    [Fact]
    public void AtomicWriteLeavesNoTemporaryFiles()
    {
        var root = Lib.Temp(true);
        new LibraryFiles(root).Write(LibraryFile.Photos, [Lib.Photo()]);
        Assert.Equal(["photos.json"], Directory.GetFiles(Path.Combine(root, "Progress")).Select(Path.GetFileName));
    }

    [Fact]
    public void ConflictCopiesIgnored()
    {
        var root = Lib.Temp(true);
        var copy = Path.Combine(root, "Progress/checks 2.json");
        File.WriteAllText(copy, "junk");
        Assert.IsType<Missing<IReadOnlyList<PlanTick>>>(new LibraryFiles(root).Read(LibraryFile.Checks));
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public void WritingToAMissingLibraryFailsAndCreatesNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "lw-gone-" + Guid.NewGuid());
        Assert.Throws<LibraryUnavailableException>(() => new LibraryFiles(root).Write(LibraryFile.Photos, [Lib.Photo()]));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void UnreadableFileThrowsAndIsLeftAlone()
    {
        if (OperatingSystem.IsWindows()) return; // No POSIX modes there; ALockedFileIsRetriedThenReported covers it.
        var root = Lib.Temp(true);
        var url = Path.Combine(root, "Progress/checks.json");
        File.WriteAllText(url, """{"version":1,"items":[]}""");
        File.SetUnixFileMode(url, UnixFileMode.None);
        try
        {
            Assert.Equal("checks.json", Assert.Throws<UnreadableException>(() => new LibraryFiles(root).Read(LibraryFile.Checks)).File);
            Assert.True(File.Exists(url));
        }
        finally { File.SetUnixFileMode(url, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    [Fact]
    public void ASecondDamagedFileDoesNotReplaceTheFirst()
    {
        var root = Lib.Temp(true);
        var url = Path.Combine(root, "Progress/checks.json");
        var files = new LibraryFiles(root);
        File.WriteAllText(url, "first");
        files.Read(LibraryFile.Checks);
        File.WriteAllText(url, "second");
        var aside = Assert.IsType<Damaged<IReadOnlyList<PlanTick>>>(files.Read(LibraryFile.Checks)).SetAsideAs;
        Assert.Equal("first", File.ReadAllText(Path.Combine(root, "Progress/checks.damaged.json")));
        Assert.Equal("second", File.ReadAllText(aside));
    }

    [Fact]
    public void RetryGivesUpAfterAboutTwoSeconds()
    {
        var calls = 0;
        var started = DateTime.UtcNow;
        Assert.Throws<IOException>(() => FileRetry.Run(() => { calls++; throw new IOException("locked"); }));
        Assert.InRange((DateTime.UtcNow - started).TotalSeconds, 1.5, 4);
        Assert.True(calls > 2);
    }

    [Fact]
    public void RetrySucceedsOnceTheFileIsFree()
    {
        var calls = 0;
        FileRetry.Run(() => { if (++calls < 3) throw new UnauthorizedAccessException(); });
        Assert.Equal(3, calls);
    }

    [Fact]
    public void RetryDoesNotWaitForAMissingFile()
    {
        var calls = 0;
        Assert.Throws<FileNotFoundException>(() => FileRetry.Run(() => { calls++; throw new FileNotFoundException(); }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ALockedFileIsRetriedThenReported()
    {
        if (!OperatingSystem.IsWindows()) return; // Only Windows locks files against replacing.
        var root = Lib.Temp(true);
        var files = new LibraryFiles(root);
        files.Write(LibraryFile.Checks, [new PlanTick("old", "d", Lib.Date)]);
        var url = Path.Combine(root, "Progress/checks.json");
        var before = File.ReadAllBytes(url);
        using (new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var started = DateTime.UtcNow;
            var failure = Record.Exception(() => files.Write(LibraryFile.Checks, [new PlanTick("new", "d", Lib.Date)]));
            Assert.True(failure is IOException or UnauthorizedAccessException, $"{failure}");
            Assert.True((DateTime.UtcNow - started).TotalSeconds >= 1.5);
            Assert.Throws<UnreadableException>(() => files.Read(LibraryFile.Checks));
        }
        Assert.Equal(before, File.ReadAllBytes(url));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "Progress")));
    }

    [Fact]
    public void LongAndNonAsciiPathsWork()
    {
        var root = Lib.Temp();
        var deep = root;
        while (deep.Length < 300) deep = Path.Combine(deep, "carpeta é 日本");
        Directory.CreateDirectory(deep);
        LibraryFolder.Create(deep, "2026-11-02", Lib.Date);
        Assert.True(deep.Length > 260);
        Assert.Equal(FolderKind.Library, LibraryFolder.Classify(deep));
        var files = new LibraryFiles(deep);
        files.Write(LibraryFile.Photos, [Lib.Photo("日本 é.jpg")]);
        Assert.Equal("日本 é.jpg", Lib.Loaded(files.Read(LibraryFile.Photos))[0].FileName);
    }
}

public class CompatibilityTests
{
    static string Library => Repo.Fixture("library");
    static readonly string[] Progress = ["photos", "benchmarks", "sets", "presets", "checks", "plan", "sessions"];

    [Fact]
    public void OpensTheMacLibraryFixture()
    {
        Assert.Equal(FolderKind.Library, LibraryFolder.Classify(Library));
        var f = new LibraryFiles(Library);
        var photos = Lib.Loaded(f.Read(LibraryFile.Photos));
        Assert.Equal(3, photos.Count);
        Assert.Equal(0.30000000000000004, photos[0].Crop.Y);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero), photos[0].ImportedAt);
        Assert.Equal("gone/é 日本.jpg", photos[2].FileName);
        Assert.True(photos[2].Missing);
        Assert.Equal(photos[0].Sha256, FileHash.Sha256(Path.Combine(Library, "Photos", photos[0].FileName)));
        var b = Lib.Loaded(f.Read(LibraryFile.Benchmarks));
        Assert.Single(b.Benchmarks);
        Assert.Equal(2, b.Attempts.Count);
        Assert.Null(b.Attempts[1].DrawingFileName);
        Assert.Equal(2, Lib.Loaded(f.Read(LibraryFile.Sets)).Count);
        Assert.Equal(3, Lib.Loaded(f.Read(LibraryFile.Presets))[0].Steps.Count);
        Assert.Equal(2, Lib.Loaded(f.Read(LibraryFile.Checks)).Count);
        Assert.Single(Lib.Loaded(f.Read(LibraryFile.Plan)).Repeats);
        Assert.Equal(150, Lib.Loaded(f.Read(LibraryFile.Sessions))[0].DrawingSeconds);
        var doc = new FreehandFiles(Path.Combine(Library, "Canvases/Untitled")).Load();
        Assert.True(doc.Greyscale);
        Assert.Equal(new Point2(12.5, -4), doc.Camera.Offset);
        var item = Assert.Single(doc.Items);
        Assert.Equal(new Size2(640, 480), item.FullSize);
        Assert.Equal(new NormalizedRect(0.1, 0.2, 0.5, 0.75), item.Crop);
    }

    [Fact]
    public void RewritesMacFilesByteForByte()
    {
        var root = Lib.Temp(true);
        Copy(root);
        foreach (var name in Progress)
            Assert.Equal(File.ReadAllBytes(Path.Combine(Library, "Progress", name + ".json")), File.ReadAllBytes(Path.Combine(root, "Progress", name + ".json")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(Library, "library.json")), File.ReadAllBytes(Path.Combine(root, "library.json")));
    }

    static void Copy(string root)
    {
        var from = new LibraryFiles(Library);
        var to = new LibraryFiles(root);
        to.Write(LibraryFile.Photos, Lib.Loaded(from.Read(LibraryFile.Photos)));
        to.Write(LibraryFile.Benchmarks, Lib.Loaded(from.Read(LibraryFile.Benchmarks)));
        to.Write(LibraryFile.Sets, Lib.Loaded(from.Read(LibraryFile.Sets)));
        to.Write(LibraryFile.Presets, Lib.Loaded(from.Read(LibraryFile.Presets)));
        to.Write(LibraryFile.Checks, Lib.Loaded(from.Read(LibraryFile.Checks)));
        to.Write(LibraryFile.Plan, Lib.Loaded(from.Read(LibraryFile.Plan)));
        to.Write(LibraryFile.Sessions, Lib.Loaded(from.Read(LibraryFile.Sessions)));
        var manifest = LibraryJson.Decode<LibraryManifest>(File.ReadAllBytes(Path.Combine(Library, "library.json")));
        File.WriteAllBytes(Path.Combine(root, "library.json"), LibraryJson.Encode(manifest));
    }

    /// <summary>The Swift suite's decodesFilesWrittenByWindows reads what this leaves.</summary>
    [Fact]
    public void WritesFilesForTheMacTests()
    {
        var root = Repo.Fixture("written-by-windows");
        var canvas = Path.Combine(root, "Canvases/Untitled");
        Directory.CreateDirectory(Path.Combine(root, "Progress"));
        Directory.CreateDirectory(Path.Combine(canvas, "Images"));
        Copy(root);
        File.Copy(Path.Combine(Library, "Canvases/Untitled/Images/board.png"), Path.Combine(canvas, "Images/board.png"), true);
        var doc = FreehandFiles.Decode(File.ReadAllBytes(Repo.Fixture("canvas-sample.json")));
        new FreehandFiles(canvas).Save(doc);
        var back = new FreehandFiles(canvas).Load();
        Assert.Equal(doc.Camera, back.Camera);
        Assert.Equal(doc.Items, back.Items);
    }
}

public class LibraryFolderTests
{
    static readonly DateTimeOffset Now = Lib.Date;

    [Fact]
    public void ClassifiesLibrary()
    {
        var url = Lib.Temp();
        LibraryFolder.Create(url, "2026-11-02", Now);
        Assert.Equal(FolderKind.Library, LibraryFolder.Classify(url));
    }

    [Theory]
    [InlineData(".DS_Store"), InlineData("desktop.ini"), InlineData("Thumbs.db")]
    public void ClassifiesEmpty(string systemFile)
    {
        var url = Lib.Temp();
        Assert.Equal(FolderKind.Empty, LibraryFolder.Classify(url));
        File.WriteAllBytes(Path.Combine(url, systemFile), []);
        Assert.Equal(FolderKind.Empty, LibraryFolder.Classify(url));
    }

    [Fact]
    public void ClassifiesOtherContents()
    {
        var url = Lib.Temp();
        File.WriteAllBytes(Path.Combine(url, "notes.txt"), []);
        Assert.Equal(FolderKind.OtherContents, LibraryFolder.Classify(url));
    }

    [Fact]
    public void ClassifiesMissingAsUnavailable() =>
        Assert.Equal(FolderKind.Unavailable, LibraryFolder.Classify(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid())));

    [Fact]
    public void ClassifiesNewerFormat()
    {
        var url = Lib.Temp();
        File.WriteAllText(Path.Combine(url, "library.json"), $$"""{"format":99,"id":"{{Guid.NewGuid()}}","createdAt":"2026-11-01T00:00:00Z"}""");
        Assert.Equal(FolderKind.NewerFormat, LibraryFolder.Classify(url));
    }

    [Fact]
    public void CreateMakesTheLayout()
    {
        var url = Lib.Temp();
        LibraryFolder.Create(url, "2026-11-02", Now);
        foreach (var name in new[] { "Photos", "Drawings", "Canvases", "Progress" }) Assert.True(Directory.Exists(Path.Combine(url, name)), name);
        Assert.True(File.Exists(Path.Combine(url, "library.json")));
        var plan = Lib.Loaded(new LibraryFiles(url).Read(LibraryFile.Plan));
        Assert.Equal("2026-11-02", plan.WeekOneStart);
        Assert.Empty(plan.Repeats);
    }

    [Fact]
    public void CreateKeepsExistingFiles()
    {
        var url = Lib.Temp();
        var keep = Path.Combine(url, "notes.txt");
        File.WriteAllText(keep, "mine");
        LibraryFolder.Create(url, "2026-11-02", Now);
        Assert.Equal("mine", File.ReadAllText(keep));
    }

    [Theory]
    [InlineData("2026-11-02"), InlineData("2026-11-05"), InlineData("2026-11-08")]
    public void MondayOfAnyWeekday(string day) => Assert.Equal("2026-11-02", LibraryFolder.MondayOf(P.D(day)));

    [Fact]
    public void ReadOnlyFolderFailsCleanly()
    {
        if (OperatingSystem.IsWindows()) return; // No POSIX modes there.
        var url = Lib.Temp();
        File.SetUnixFileMode(url, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.ThrowsAny<Exception>(() => LibraryFolder.Create(url, "2026-11-02", Now));
            Assert.Empty(Directory.GetFileSystemEntries(url));
        }
        finally { File.SetUnixFileMode(url, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    [Fact]
    public void PartlyFailedCreateLeavesNothing()
    {
        var url = Lib.Temp();
        File.WriteAllBytes(Path.Combine(url, "Progress"), []);
        Assert.ThrowsAny<Exception>(() => LibraryFolder.Create(url, "2026-11-02", Now));
        Assert.Equal(["Progress"], Directory.GetFileSystemEntries(url).Select(Path.GetFileName));
    }

    [Fact]
    public void CreateKeepsAnExistingPlan()
    {
        var url = Lib.Temp(true);
        new LibraryFiles(url).Write(LibraryFile.Plan, new PlanRecord("2026-11-02", [new PlanRepeat(1, 2, 1)]));
        LibraryFolder.Create(url, "2030-01-07", Now);
        Assert.Equal("2026-11-02", Lib.Loaded(new LibraryFiles(url).Read(LibraryFile.Plan)).WeekOneStart);
    }
}

public class PhotoScanTests
{
    static PhotoRecord Rec(string name, string hash = "h", bool missing = false) => Lib.Photo(name, hash, missing);
    static ScannedFile File_(string name, bool placeholder = false) => new(name, 10, placeholder);
    static PhotoScanResult Decide(IReadOnlyList<PhotoRecord> r, IReadOnlyList<ScannedFile> f, bool exists = true, string hash = "h") => PhotoScan.Decide(r, f, exists, _ => hash);

    [Fact]
    public void NewImageIsAdded() => Assert.Equal(["a.jpg"], Decide([], [File_("a.jpg")]).Added);

    [Fact]
    public void RenameKeepsRecord()
    {
        var r = Rec("old.jpg", "same");
        var result = Decide([r], [File_("new.jpg")], hash: "same");
        Assert.Equal((r.Id, "new.jpg"), Assert.Single(result.Adopted));
        Assert.Empty(result.Added);
        Assert.Empty(result.Missing);
    }

    [Fact]
    public void DeletedIsMarkedMissingNotRemoved()
    {
        var r = Rec("a.jpg");
        Assert.Equal([r.Id], Decide([r, Rec("b.jpg")], [File_("b.jpg")], hash: "z").Missing);
    }

    [Fact]
    public void RestoredFileReconnects()
    {
        var r = Rec("a.jpg", missing: true);
        var result = Decide([r], [File_("a.jpg")]);
        Assert.Equal([r.Id], result.Restored);
        Assert.Empty(result.Added);
    }

    [Theory]
    [InlineData(true), InlineData(false)]
    public void EmptyOrAbsentFolderChangesNothing(bool exists)
    {
        var result = Decide([Rec("a.jpg")], [], exists);
        Assert.True(result.FolderUnavailable);
        Assert.Empty(result.Missing);
        Assert.False(Decide([], []).FolderUnavailable);
    }

    [Fact]
    public void NonImagesIgnored() => Assert.Empty(Decide([], [File_("notes.txt"), File_("x.pdf")]).Added);

    [Fact]
    public void PlaceholdersSkipped() => Assert.Empty(Decide([], [File_("a.jpg", true), File_(".b.jpg.icloud")]).Added);

    [Fact]
    public void PlaceholderOfAKnownPhotoIsNotMissing() =>
        Assert.Empty(Decide([Rec("a.jpg"), Rec("b.jpg")], [File_("a.jpg", true), File_("b.jpg")]).Missing);

    [Fact]
    public void HiddenFilesIgnored() => Assert.Empty(Decide([], [File_(".DS_Store"), File_(".hidden.jpg")]).Added);

    [Fact]
    public void OnlyNewFilesAreHashed()
    {
        var calls = 0;
        PhotoScan.Decide([Rec("a.jpg"), Rec("gone.jpg", "g")], [File_("a.jpg"), File_("new.jpg")], true, _ => { calls++; return "n"; });
        Assert.Equal(1, calls);
    }

    [Fact]
    public void NoHashingWithoutAMissingRecord()
    {
        var calls = 0;
        PhotoScan.Decide([Rec("a.jpg")], [File_("a.jpg"), File_("new.jpg")], true, _ => { calls++; return "n"; });
        Assert.Equal(0, calls);
    }

    [Fact]
    public void IdenticalCopiesAdoptOnce()
    {
        var result = Decide([Rec("old.jpg", "same")], [File_("one.jpg"), File_("two.jpg")], hash: "same");
        Assert.Single(result.Adopted);
        Assert.Single(result.Added);
        Assert.Equal(new HashSet<string> { "one.jpg", "two.jpg" }, result.Added.Append(result.Adopted[0].NewName).ToHashSet());
    }

    [Fact]
    public void AHashFailureCountsAsANewFile() =>
        Assert.Equal(["n.jpg"], PhotoScan.Decide([Rec("gone.jpg")], [File_("n.jpg")], true, _ => throw new IOException()).Added);

    [Theory]
    [InlineData("a.JPG"), InlineData("b.jpeg"), InlineData("c.png"), InlineData("d.heic"), InlineData("e.webp"), InlineData("f.tiff"), InlineData("g.gif"), InlineData("h.bmp")]
    public void ImageNames(string name) => Assert.True(PhotoScan.IsImageName(name));

    [Theory]
    [InlineData("a.txt"), InlineData("b"), InlineData("c.json")]
    public void NotImageNames(string name) => Assert.False(PhotoScan.IsImageName(name));

    [Fact]
    public void UniqueNameHandlesCaseAndUnicode()
    {
        Assert.Equal("pose.jpg", PhotoScan.UniqueName("pose.jpg", []));
        Assert.Equal("pose 2.jpg", PhotoScan.UniqueName("pose.jpg", ["POSE.JPG"]));
        Assert.Equal("pose 3.jpg", PhotoScan.UniqueName("pose.jpg", ["pose.jpg", "pose 2.jpg"]));
        Assert.Equal("café 2.jpg", PhotoScan.UniqueName("café.jpg", ["café.jpg"]));
    }

    [Fact]
    public void Sha256OfAKnownFile()
    {
        var url = Path.Combine(Lib.Temp(), "h");
        File.WriteAllText(url, "abc", new UTF8Encoding(false));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", FileHash.Sha256(url));
    }

    [Fact]
    public void RecordsLookIncompleteWhenSomethingPointsPastThem()
    {
        var r = Rec("a.jpg");
        Assert.True(PhotoScan.RecordsLookComplete([r], new HashSet<Guid> { r.Id }));
        Assert.True(PhotoScan.RecordsLookComplete([], new HashSet<Guid>()));
        Assert.False(PhotoScan.RecordsLookComplete([], new HashSet<Guid> { Guid.NewGuid() }));
        Assert.False(PhotoScan.RecordsLookComplete([r], new HashSet<Guid> { r.Id, Guid.NewGuid() }));
    }

    [Fact]
    public void NfdNameOnDiskMatchesNfcRecord()
    {
        var r = Rec("José.jpg");
        var result = Decide([r, Rec("b.jpg")], [File_("José.jpg"), File_("b.jpg")]);
        Assert.Empty(result.Missing);
        Assert.Empty(result.Added);
        Assert.Empty(result.Adopted);

        var folder = Lib.Temp();
        File.WriteAllBytes(Path.Combine(folder, "José.jpg"), [1]);
        var found = PhotoScan.PathOnDisk(folder, "José.jpg");
        Assert.Equal([1], File.ReadAllBytes(found));
        Assert.Equal(Path.Combine(folder, "absent.jpg"), PhotoScan.PathOnDisk(folder, "absent.jpg"));
    }
}

public class CanvasDirectoryTests
{
    static CanvasDirectory Dir() => new(Lib.Temp());

    [Fact]
    public void FirstIsUntitled() => Assert.Equal("Untitled", Dir().Create(null));

    [Fact]
    public void NextIsUntitled2()
    {
        var d = Dir();
        d.Create(null);
        Assert.Equal("Untitled 2", d.Create(null));
        Assert.Equal(["Untitled", "Untitled 2"], d.List());
    }

    [Fact]
    public void ListSortsNumbersNaturally()
    {
        var d = Dir();
        foreach (var n in new[] { "Untitled 10", "untitled 2", "Apple" }) d.Create(n);
        Assert.Equal(["Apple", "untitled 2", "Untitled 10"], d.List());
    }

    [Fact]
    public void RenameMovesFolder()
    {
        var d = Dir();
        d.Create("Old");
        d.Rename("Old", "New");
        Assert.Equal(["New"], d.List());
    }

    [Theory]
    [InlineData("a/b"), InlineData("a:b"), InlineData(".hidden"), InlineData(""), InlineData("   "), InlineData("."), InlineData("..")]
    public void RejectsSlashColonDotsAndEmpty(string bad) =>
        Assert.Equal(CanvasNameError.Invalid, Assert.Throws<CanvasNameException>(() => Dir().Create(bad)).Error);

    [Theory]
    [InlineData("CON"), InlineData("nul"), InlineData("COM1"), InlineData("lpt9"), InlineData("aux.txt"), InlineData("a?"), InlineData("a."), InlineData("a<b"), InlineData("a>b"),
     InlineData("a\"b"), InlineData("a\\b"), InlineData("a|b"), InlineData("a*b")]
    public void WindowsReservedCanvasNamesRejected(string bad) =>
        Assert.Equal(CanvasNameError.Invalid, Assert.Throws<CanvasNameException>(() => Dir().Create(bad)).Error);

    [Fact]
    public void ATrailingSpaceNeverReachesTheDisk() => Assert.Equal("a", Dir().Create("a "));

    [Fact]
    public void NamesAreTrimmed() => Assert.Equal("Hands", Dir().Create("  Hands  "));

    [Fact]
    public void FoldersWithUnusableNamesAreLeftAloneAndReported()
    {
        if (OperatingSystem.IsWindows()) return; // Windows can't make these to begin with.
        var d = Dir();
        d.Create("Good");
        Directory.CreateDirectory(Path.Combine(d.CanvasesRoot, "a?b"));
        Assert.Equal(["Good"], d.List());
        Assert.Equal(["a?b"], d.UnusableNames());
        Assert.True(Directory.Exists(Path.Combine(d.CanvasesRoot, "a?b")));
    }

    [Fact]
    public void DuplicateCopiesImages()
    {
        var d = Dir();
        d.Create("Refs");
        var images = Path.Combine(d.PathOf("Refs"), "Images");
        Directory.CreateDirectory(images);
        File.WriteAllText(Path.Combine(images, "a.jpg"), "x");
        var copy = d.Duplicate("Refs");
        Assert.Equal("Refs copy", copy);
        Assert.True(File.Exists(Path.Combine(d.PathOf(copy), "Images/a.jpg")));
        Assert.Equal("Refs copy 2", d.Duplicate("Refs"));
    }

    [Fact]
    public void EnsureOneCreatesWhenEmpty()
    {
        var d = Dir();
        Assert.Equal("Untitled", d.EnsureOne());
        d.Create("Another");
        Assert.Equal("Another", d.EnsureOne());
        Assert.Equal(2, d.List().Count);
    }

    [Fact]
    public void ListIgnoresFiles()
    {
        var d = Dir();
        d.Create("Real");
        File.WriteAllBytes(Path.Combine(d.CanvasesRoot, ".DS_Store"), []);
        File.WriteAllBytes(Path.Combine(d.CanvasesRoot, "note.txt"), []);
        Assert.Equal(["Real"], d.List());
    }

    [Fact]
    public void CaseOnlyRenameSucceeds()
    {
        var d = Dir();
        d.Create("hands");
        d.Rename("hands", "Hands");
        Assert.Equal(["Hands"], d.List());
    }

    [Fact]
    public void UnicodeFormsAreOneName()
    {
        var d = Dir();
        d.Create("café");
        Assert.Equal(CanvasNameError.Duplicate, Assert.Throws<CanvasNameException>(() => d.Create("café")).Error);
    }

    [Fact]
    public void TrueDuplicateRefused()
    {
        var d = Dir();
        d.Create("A");
        d.Create("B");
        Assert.Equal(CanvasNameError.Duplicate, Assert.Throws<CanvasNameException>(() => d.Rename("B", "a")).Error);
        Assert.Equal(["A", "B"], d.List());
    }

    [Fact]
    public void CreateFailsRatherThanRecreatingAMissingLibrary()
    {
        var root = Path.Combine(Path.GetTempPath(), "gone-" + Guid.NewGuid());
        Assert.ThrowsAny<IOException>(() => new CanvasDirectory(Path.Combine(root, "Canvases")).Create("A"));
        Assert.False(Directory.Exists(root));
    }
}
