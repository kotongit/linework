using Linework.Core.Library;
using Linework.Core.Plan;
using Linework.Core.Session;

namespace Linework.App.Library;

/// <summary>The open library's data, for views to read, and the only thing that writes to it. UI thread only.</summary>
public sealed class LibraryStore : ObservableObject
{
    enum Part { Photos, Benchmarks, Sets, Presets, Sessions, Checks, Plan }

    static readonly Part[] AllParts = Enum.GetValues<Part>();
    public static readonly TimeSpan NoticeTime = TimeSpan.FromSeconds(8);
    static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500), RetryDelay = TimeSpan.FromSeconds(30);
    const string DefaultPreset = "Default class";

    readonly ILibraryHost _host;
    readonly HashSet<Part> _dirty = [];
    /// <summary>On disk from a newer app. Memory carries on, and these aren't saved over.</summary>
    readonly HashSet<Part> _newer = [];
    readonly HashSet<Part> _failed = [];
    readonly Dictionary<Part, DateTime?> _loadedDates = [];
    IDisposable? _saveTimer, _noticeTimer;
    bool _isReloading;
    string? _notice;

    public string Root { get; }
    public LibraryManifest Manifest { get; }
    public LibraryFiles Files { get; }

    public List<PhotoRecord> Photos { get; private set; } = [];
    public BenchmarksFile BenchmarkData { get; private set; } = BenchmarksFile.Empty;
    public List<SetRecord> Sets { get; private set; } = [];
    public List<PresetRecord> Presets { get; private set; } = [];
    public List<SessionRecord> Sessions { get; private set; } = [];
    public List<PlanTick> Ticks { get; private set; } = [];
    public HashSet<string> TickKeys { get; private set; } = [];
    public PlanRecord Plan { get; private set; }
    public PlanSchedule Schedule { get; private set; }

    /// <summary>A save that failed, for the user to see. Clears when a save succeeds.</summary>
    public string? SaveFailure { get; private set; }
    public List<string> DamagedFiles { get; } = [];

    /// <summary>A passing message. Clears itself, so an old one can't hide a newer problem.</summary>
    public string? Notice
    {
        get => _notice;
        set
        {
            _notice = value;
            _noticeTimer?.Dispose();
            if (value != null) _noticeTimer = _host.After(NoticeTime, () => Notice = null);
            NotifyChanged();
        }
    }

    /// <summary>While these run, the File Explorer check waits, so it can't mistake a file mid-import for a new one.</summary>
    public int ImportsRunning { get; set; }
    public bool IsScanning { get; set; }

    /// <summary>Throws NewerFormatException or UnreadableException if the library can't be read by this version of the app.</summary>
    public LibraryStore(string root, ILibraryHost host)
    {
        Root = root;
        _host = host;
        Files = new LibraryFiles(root);
        Manifest = LibraryJson.Decode<LibraryManifest>(File.ReadAllBytes(Path.Combine(root, LibraryFolder.ManifestName)));
        if (Manifest.Format > LibraryManifest.CurrentFormat) throw new NewerFormatException(LibraryFolder.ManifestName);
        Plan = new PlanRecord(LibraryFolder.MondayOf(host.Today), []);
        Schedule = MakeSchedule(Plan);
        LoadAll();
    }

    PlanSchedule MakeSchedule(PlanRecord plan) =>
        new(PracticePlan.Shared, PlanDayKey.Date(plan.WeekOneStart) ?? _host.Today, plan.Repeats);

    // Loading

    T Value<T>(LibraryFile<T> file, T fallback)
    {
        switch (Files.Read(file))
        {
            case Loaded<T> loaded:
                return loaded.Value;
            case Damaged<T>:
                if (!DamagedFiles.Contains(file.Name)) DamagedFiles.Add(file.Name);
                // What's in memory is good, so a reload keeps it rather than taking the fallback.
                if (_isReloading) throw new UnreadableException(file.Name);
                return fallback;
            default:
                return fallback;
        }
    }

    void LoadAll()
    {
        foreach (var part in AllParts) Load(part);
        if (Presets.Count == 0 && Files.Read(LibraryFile.Presets) is Missing<IReadOnlyList<PresetRecord>>)
        {
            Presets = [new PresetRecord(Guid.NewGuid(), DefaultPreset, QueueBuilder.DefaultClass)];
            MarkDirty(Part.Presets);
        }
    }

    void Load(Part part)
    {
        switch (part)
        {
            case Part.Photos: Photos = [.. Value<IReadOnlyList<PhotoRecord>>(LibraryFile.Photos, [])]; break;
            case Part.Benchmarks: BenchmarkData = Value(LibraryFile.Benchmarks, BenchmarksFile.Empty); break;
            case Part.Sets: Sets = [.. Value<IReadOnlyList<SetRecord>>(LibraryFile.Sets, [])]; break;
            case Part.Presets: Presets = [.. Value<IReadOnlyList<PresetRecord>>(LibraryFile.Presets, [])]; break;
            case Part.Sessions: Sessions = [.. Value<IReadOnlyList<SessionRecord>>(LibraryFile.Sessions, [])]; break;
            case Part.Checks:
                Ticks = [.. Value<IReadOnlyList<PlanTick>>(LibraryFile.Checks, [])];
                TickKeys = Ticks.Select(t => t.Key).ToHashSet();
                break;
            case Part.Plan:
                Plan = Value(LibraryFile.Plan, Plan);
                Schedule = MakeSchedule(Plan);
                break;
        }
        _loadedDates[part] = DiskDate(part);
        // Without a saved start, Week 1 would drift to the current week on every launch.
        if (part == Part.Plan && _loadedDates[part] == null) MarkDirty(Part.Plan);
    }

    DateTime? DiskDate(Part part) => part switch
    {
        Part.Photos => Files.ModificationDate(LibraryFile.Photos),
        Part.Benchmarks => Files.ModificationDate(LibraryFile.Benchmarks),
        Part.Sets => Files.ModificationDate(LibraryFile.Sets),
        Part.Presets => Files.ModificationDate(LibraryFile.Presets),
        Part.Sessions => Files.ModificationDate(LibraryFile.Sessions),
        Part.Checks => Files.ModificationDate(LibraryFile.Checks),
        _ => Files.ModificationDate(LibraryFile.Plan),
    };

    /// <summary>Picks up files changed outside the app. The latest save wins; anything unsaved here is kept.</summary>
    public void ReloadIfChanged()
    {
        var changed = false;
        foreach (var part in AllParts)
        {
            if (_dirty.Contains(part)) continue;
            var onDisk = DiskDate(part);
            if (onDisk == null || onDisk == _loadedDates.GetValueOrDefault(part)) continue;
            _isReloading = true;
            try
            {
                Load(part);
                changed = true;
            }
            catch (UnreadableException) when (DiskDate(part) == null)
            {
                // The damaged file was set aside, so what's in memory goes back in its place.
                MarkDirty(part);
                changed = true;
            }
            catch (NewerFormatException)
            {
                // From a newer app: leave the file and memory alone, and don't save over it.
                _newer.Add(part);
                _loadedDates[part] = onDisk;
            }
            catch (Exception e) when (e is UnreadableException or IOException or UnauthorizedAccessException)
            {
                // Out of reach just now: leave both alone. The date stays old, so the next look tries again.
            }
            finally
            {
                _isReloading = false;
            }
        }
        if (changed) NotifyChanged();
    }

    // Saving

    void MarkDirty(Part part)
    {
        _dirty.Add(part);
        _saveTimer?.Dispose();
        _saveTimer = _host.After(SaveDelay, () => Flush());
        NotifyChanged();
    }

    /// <summary>Returns false if anything couldn't be saved.</summary>
    public bool Flush()
    {
        _saveTimer?.Dispose();
        _saveTimer = null;
        foreach (var part in _dirty.ToList()) Save(part);
        // What couldn't be saved tries again by itself, so it isn't left to a next edit that may not come.
        if (_dirty.Count > 0) _saveTimer = _host.After(RetryDelay, () => Flush());
        NotifyChanged();
        return _dirty.Count == 0;
    }

    void SaveNow(params Part[] parts)
    {
        foreach (var part in parts) _dirty.Add(part);
        foreach (var part in parts) Save(part);
        NotifyChanged();
    }

    void Save(Part part)
    {
        if (_newer.Contains(part))
        {
            SaveFailure = "Part of your library was saved by a newer version of Linework, so changes to it aren't being saved here.";
            return;
        }
        try
        {
            switch (part)
            {
                case Part.Photos: Files.Write<IReadOnlyList<PhotoRecord>>(LibraryFile.Photos, Photos); break;
                case Part.Benchmarks: Files.Write(LibraryFile.Benchmarks, BenchmarkData); break;
                case Part.Sets: Files.Write<IReadOnlyList<SetRecord>>(LibraryFile.Sets, Sets); break;
                case Part.Presets: Files.Write<IReadOnlyList<PresetRecord>>(LibraryFile.Presets, Presets); break;
                case Part.Sessions: Files.Write<IReadOnlyList<SessionRecord>>(LibraryFile.Sessions, Sessions); break;
                case Part.Checks: Files.Write<IReadOnlyList<PlanTick>>(LibraryFile.Checks, Ticks); break;
                case Part.Plan: Files.Write(LibraryFile.Plan, Plan); break;
            }
            _dirty.Remove(part);
            _loadedDates[part] = DiskDate(part);
            if (_dirty.Count == 0 || !_failed.Overlaps(_dirty)) SaveFailure = null;
            _failed.Remove(part);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or LibraryUnavailableException)
        {
            // Stays dirty and in memory, so the next save tries again.
            _failed.Add(part);
            SaveFailure = Directory.Exists(Root) ? "Couldn't save your library." : "Your library's folder can't be found, so changes aren't being saved.";
        }
    }

    // Plan

    public bool IsTicked(PlanDayRef r, PlanBlockKind kind) => TickKeys.Contains(Schedule.TickKey(r, kind));

    /// <summary>Returns true if the block is now ticked.</summary>
    public bool ToggleTick(PlanDayRef r, PlanBlockKind kind)
    {
        var key = Schedule.TickKey(r, kind);
        if (TickKeys.Contains(key))
        {
            Ticks.RemoveAll(t => t.Key == key);
            TickKeys.Remove(key);
            SaveNow(Part.Checks);
            return false;
        }
        AddTick(key, r);
        return true;
    }

    /// <summary>Ticks the block if it isn't already. Returns true if this added the tick.</summary>
    public bool EnsureTick(PlanDayRef r, PlanBlockKind kind)
    {
        var key = Schedule.TickKey(r, kind);
        if (TickKeys.Contains(key)) return false;
        AddTick(key, r);
        return true;
    }

    void AddTick(string key, PlanDayRef r)
    {
        Ticks.Add(new PlanTick(key, PlanTick.CountsOnFor(Schedule.DateOf(r), _host.Today), _host.Now));
        TickKeys.Add(key);
        SaveNow(Part.Checks);
    }

    public HashSet<DateOnly> PracticeDays =>
        PlanProgress.PracticeDays(Ticks, Sessions.Select(s => DateOnly.FromDateTime(s.StartedAt.ToLocalTime().DateTime)));

    /// <summary>Repeats the last weekCount weeks, right after the week containing the date.</summary>
    public void AddRepeat(int weekCount, DateOnly date)
    {
        if (Schedule.RefOn(date) is not PlanRef p) return;
        var number = Plan.Repeats.Select(r => r.Number).DefaultIfEmpty(0).Max() + 1;
        Plan = Plan with { Repeats = [.. Plan.Repeats, new PlanRepeat(number, p.SequenceIndex, weekCount)] };
        Schedule = MakeSchedule(Plan);
        SaveNow(Part.Plan);
    }

    /// <summary>Removes the latest repeat and any ticks on its lessons.</summary>
    public void UndoLastRepeat()
    {
        if (Plan.Repeats.Count == 0) return;
        var last = Plan.Repeats[^1];
        Plan = Plan with { Repeats = Plan.Repeats.Take(Plan.Repeats.Count - 1).ToList() };
        var suffix = $"-r{last.Number}";
        Ticks.RemoveAll(t => t.Key.EndsWith(suffix, StringComparison.Ordinal) || t.Key.Contains(suffix + "c", StringComparison.Ordinal));
        TickKeys = Ticks.Select(t => t.Key).ToHashSet();
        Schedule = MakeSchedule(Plan);
        _dirty.Add(Part.Plan);
        _dirty.Add(Part.Checks);
        Flush();
    }

    // Photos

    public IReadOnlyList<PhotoRecord> VisiblePhotos => Photos.Where(p => !p.Missing).ToList();

    public PhotoRecord? Photo(Guid id) => Photos.FirstOrDefault(p => p.Id == id);

    public string PhotosDir => Path.Combine(Root, "Photos");
    public string DrawingsDir => Path.Combine(Root, "Drawings");
    public string CanvasesDir => Path.Combine(Root, "Canvases");

    public string PhotoPath(PhotoRecord record) => PhotoScan.PathOnDisk(PhotosDir, record.FileName);
    public string DrawingPath(string name) => PhotoScan.PathOnDisk(DrawingsDir, name);

    public void AddPhotos(IReadOnlyList<PhotoRecord> records, Guid? toSet)
    {
        if (records.Count == 0) return;
        Photos.AddRange(records);
        MarkDirty(Part.Photos);
        var index = Sets.FindIndex(s => s.Id == toSet);
        if (index >= 0)
        {
            Sets[index] = Sets[index] with { PhotoIDs = [.. Sets[index].PhotoIDs, .. records.Select(r => r.Id)] };
            MarkDirty(Part.Sets);
        }
    }

    public void Update(Guid id, Func<PhotoRecord, PhotoRecord> change)
    {
        var index = Photos.FindIndex(p => p.Id == id);
        if (index < 0) return;
        Photos[index] = change(Photos[index]);
        MarkDirty(Part.Photos);
    }

    /// <summary>Moves the photo and its attempts' drawings to the Recycle Bin, then removes their records. A failed move changes nothing.</summary>
    public void DeletePhoto(Guid id)
    {
        if (Photo(id) is not { } record) return;
        var attempts = BenchmarkData.Attempts.Where(a => a.PhotoID == id).ToList();
        // The photo goes last, so a failure part-way can't leave its record without a file.
        var paths = attempts.Where(a => a.DrawingFileName != null).Select(a => DrawingPath(a.DrawingFileName!)).Append(PhotoPath(record)).ToList();
        try
        {
            _host.MoveToRecycleBin(paths);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Notice = "Couldn't move that photo to the Recycle Bin.";
            return;
        }
        Photos.RemoveAll(p => p.Id == id);
        BenchmarkData = new BenchmarksFile(BenchmarkData.Benchmarks.Where(b => b.PhotoID != id).ToList(), BenchmarkData.Attempts.Where(a => a.PhotoID != id).ToList());
        Sets = Sets.Select(s => s with { PhotoIDs = s.PhotoIDs.Where(p => p != id).ToList() }).ToList();
        _dirty.Add(Part.Photos);
        _dirty.Add(Part.Benchmarks);
        _dirty.Add(Part.Sets);
        Flush();
    }

    // Sets

    public SetRecord CreateSet(string name)
    {
        var set = new SetRecord(Guid.NewGuid(), name, _host.Now, []);
        Sets.Add(set);
        MarkDirty(Part.Sets);
        return set;
    }

    public void RenameSet(Guid id, string name)
    {
        var index = Sets.FindIndex(s => s.Id == id);
        if (index < 0) return;
        Sets[index] = Sets[index] with { Name = name };
        MarkDirty(Part.Sets);
    }

    public void DeleteSet(Guid id)
    {
        Sets.RemoveAll(s => s.Id == id);
        MarkDirty(Part.Sets);
    }

    public void SetPhoto(Guid photoID, Guid setID, bool member)
    {
        var index = Sets.FindIndex(s => s.Id == setID);
        if (index < 0) return;
        var ids = Sets[index].PhotoIDs.Where(p => p != photoID).ToList();
        if (member) ids.Add(photoID);
        Sets[index] = Sets[index] with { PhotoIDs = ids };
        MarkDirty(Part.Sets);
    }

    // Benchmarks

    public BenchmarkRecord? Benchmark(Guid photoID) => BenchmarkData.Benchmarks.FirstOrDefault(b => b.PhotoID == photoID);
    public bool IsBenchmark(Guid photoID) => Benchmark(photoID) != null;
    public IReadOnlyList<AttemptRecord> Attempts(Guid photoID) => BenchmarkData.Attempts.Where(a => a.PhotoID == photoID).OrderBy(a => a.Date).ToList();

    /// <summary>Unmarking keeps attempts, so marking it again brings them back.</summary>
    public void ToggleBenchmark(Guid photoID)
    {
        var benchmarks = IsBenchmark(photoID)
            ? BenchmarkData.Benchmarks.Where(b => b.PhotoID != photoID).ToList()
            : [.. BenchmarkData.Benchmarks, new BenchmarkRecord(photoID, 120, 10, _host.Now)];
        BenchmarkData = BenchmarkData with { Benchmarks = benchmarks };
        MarkDirty(Part.Benchmarks);
    }

    public void UpdateBenchmark(Guid photoID, int? seconds = null, int? interval = null)
    {
        BenchmarkData = BenchmarkData with
        {
            Benchmarks = BenchmarkData.Benchmarks.Select(b => b.PhotoID != photoID ? b : b with { Seconds = seconds ?? b.Seconds, Interval = interval ?? b.Interval }).ToList(),
        };
        MarkDirty(Part.Benchmarks);
    }

    /// <summary>When the benchmark's interval counts from.</summary>
    public DateTimeOffset BenchmarkSince(BenchmarkRecord record) => Attempts(record.PhotoID).LastOrDefault()?.Date ?? record.MarkedAt;

    public IReadOnlyList<DueBenchmark> DueBenchmarks()
    {
        var dates = Sessions.Select(s => s.StartedAt).ToList();
        return BenchmarkData.Benchmarks
            .Where(r => Photo(r.PhotoID) is { Missing: false } && BenchmarkSchedule.IsDue(BenchmarkSince(r), r.Interval, dates))
            .Select(r => new DueBenchmark(r.PhotoID, r.Seconds, Attempts(r.PhotoID).Count + 1)).ToList();
    }

    /// <summary>Copies a drawing into Drawings/ under a readable name. Throws InvalidDataException if it isn't an image.</summary>
    string CopyDrawing(string source)
    {
        if (!_host.IsReadableImage(source)) throw new InvalidDataException("Not an image");
        var existing = Directory.Exists(DrawingsDir) ? Directory.EnumerateFileSystemEntries(DrawingsDir).Select(p => Path.GetFileName(p)!) : [];
        var name = PhotoScan.UniqueName(Path.GetFileName(source), existing);
        // If the library has gone, this fails rather than making a new one.
        if (!Directory.Exists(Root)) throw new LibraryUnavailableException();
        Directory.CreateDirectory(DrawingsDir);
        File.Copy(source, Path.Combine(DrawingsDir, name));
        return name;
    }

    public void AddAttempt(Guid photoID, Guid sessionID, string? drawing)
    {
        var name = drawing != null ? CopyDrawing(drawing) : null;
        BenchmarkData = BenchmarkData with { Attempts = [.. BenchmarkData.Attempts, new AttemptRecord(Guid.NewGuid(), photoID, _host.Now, sessionID, name)] };
        SaveNow(Part.Benchmarks);
    }

    public void AttachDrawing(Guid attemptID, string source)
    {
        if (BenchmarkData.Attempts.All(a => a.Id != attemptID)) return;
        var name = CopyDrawing(source);
        BenchmarkData = BenchmarkData with { Attempts = BenchmarkData.Attempts.Select(a => a.Id == attemptID ? a with { DrawingFileName = name } : a).ToList() };
        SaveNow(Part.Benchmarks);
    }

    // Sessions and presets

    public void RecordSession(SessionRecord record)
    {
        Sessions.Add(record);
        SaveNow(Part.Sessions);
    }

    public PresetRecord AddPreset(string name, IReadOnlyList<ClassStep> steps)
    {
        var preset = new PresetRecord(Guid.NewGuid(), name, steps);
        Presets.Add(preset);
        MarkDirty(Part.Presets);
        return preset;
    }

    // File Explorer changes

    /// <summary>False if the photo records can't be trusted, in which case the File Explorer check must wait.</summary>
    public bool PhotoRecordsLookComplete
    {
        get
        {
            var referenced = Sets.SelectMany(s => s.PhotoIDs).Concat(BenchmarkData.Benchmarks.Select(b => b.PhotoID)).Concat(BenchmarkData.Attempts.Select(a => a.PhotoID)).ToHashSet();
            return !DamagedFiles.Contains(LibraryFile.Photos.Name) && !PhotoRecordsWereSetAside && PhotoScan.RecordsLookComplete(Photos, referenced);
        }
    }

    /// <summary>A set-aside photos file from an earlier run still means the records were lost. Adding files now would give them new
    /// identities and lose their crops, so the check waits until the user has dealt with it.</summary>
    bool PhotoRecordsWereSetAside
    {
        get
        {
            try
            {
                return Directory.EnumerateFiles(Path.Combine(Root, "Progress"), "photos.damaged*.json").Any();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public void ApplyScan(PhotoScanResult result, IReadOnlyList<PhotoRecord> added)
    {
        if (result.FolderUnavailable)
        {
            Notice = "Your Photos folder is out of reach, so nothing was changed.";
            return;
        }
        foreach (var (id, name) in result.Adopted)
        {
            var index = Photos.FindIndex(p => p.Id == id);
            if (index >= 0) Photos[index] = Photos[index] with { FileName = name, Missing = false };
        }
        foreach (var id in result.Missing)
        {
            var index = Photos.FindIndex(p => p.Id == id);
            if (index >= 0) Photos[index] = Photos[index] with { Missing = true };
        }
        foreach (var id in result.Restored)
        {
            var index = Photos.FindIndex(p => p.Id == id);
            if (index >= 0) Photos[index] = Photos[index] with { Missing = false };
        }
        Photos.AddRange(added);
        if (result.Adopted.Count > 0 || result.Missing.Count > 0 || result.Restored.Count > 0 || added.Count > 0) MarkDirty(Part.Photos);
    }
}
