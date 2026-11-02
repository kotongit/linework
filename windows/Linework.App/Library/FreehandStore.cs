using Linework.Core.Crop;
using Linework.Core.Freehand;
using Linework.Core.Library;

namespace Linework.App.Library;

public enum FreehandTool { None, Resize, Rotate }

/// <summary>Owns the Freehand canvas: the document, its edits, the debounced save and file imports.
/// All points are in the canvas area's top-left coordinates, in screen points. One canvas folder is open at a time.</summary>
public sealed class FreehandStore(CanvasDirectory canvases, Guid libraryId, ILibraryHost host, Settings settings) : ObservableObject
{
    abstract record Drag;
    sealed record PanDrag : Drag;
    sealed record ItemDrag(Guid Id) : Drag;
    /// <summary>Grab is how far from the corner the press landed, so the corner doesn't jump to the pointer.</summary>
    sealed record ResizeDrag(Guid Id, Corner Corner, Size2 Grab) : Drag;
    sealed record RotateDrag(Guid Id) : Drag;

    sealed record Copied(string FileName, double Width, double Height);

    /// <summary>Hit area of a handle: 24pt across, at any zoom.</summary>
    public const double HandleHitSize = 24;
    static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500), SkipNoteDuration = TimeSpan.FromSeconds(4);
    const int MaxConcurrentImports = 4;
    const double ZoomStepFactor = 1.25, NewImageLongEdge = 400, DropStagger = 32, DragThreshold = 3;

    Drag? _drag;
    Point2 _dragLast, _dragStart;
    bool _dragMoved, _isLoaded, _isDirty, _saveFailed, _readFailed;
    string? _skipNote, _unusable;
    IDisposable? _saveTimer, _skipTimer;
    FreehandFiles _files = new(canvases.CanvasesRoot);

    public FreehandDocument Document { get; private set; } = FreehandDocument.Empty;
    public Guid? SelectedId { get; set; }
    public FreehandTool Tool { get; set; }
    /// <summary>Where the circular menu is open, in screen points.</summary>
    public Point2? RingAt { get; private set; }
    public Size2 Viewport { get; set; }
    /// <summary>The floating bar's width, which the circular menu keeps clear of.</summary>
    public double BarWidth { get; set; }
    /// <summary>The image whose crop window is open.</summary>
    public Guid? CropId { get; private set; }
    public string CurrentName { get; private set; } = "";
    public IReadOnlyList<string> Names { get; private set; } = [];
    public CanvasDirectory Canvases => canvases;
    public FreehandFiles Files => _files;
    public bool IsInteracting => _drag != null;

    /// <summary>What the floating bar should say. A skip note shows over a save failure, which returns after it.</summary>
    public string? Status => _skipNote
        ?? (_readFailed ? "Couldn't read this canvas" : _saveFailed ? "Couldn't save your canvas" : null)
        ?? (_unusable is { } unusable ? $"“{unusable}” has a name Windows can't use, so it was left alone" : null);

    // Canvases

    public void RefreshList()
    {
        Names = canvases.List();
        // Kept, because the bar reads Status every frame while panning.
        _unusable = canvases.UnusableNames().FirstOrDefault();
        if (_readFailed && CurrentName.Length > 0) LoadCurrent();
        NotifyChanged();
    }

    void Remember(string name)
    {
        var key = libraryId.ToString();
        settings.Update(v => v with { LastCanvas = new Dictionary<string, string>(v.LastCanvas) { [key] = name } });
    }

    void LoadCurrent()
    {
        try
        {
            Document = _files.Load();
            _readFailed = false;
            _isDirty = false;
        }
        catch (Exception e) when (e is UnreadableException or IOException or UnauthorizedAccessException)
        {
            // Locked or offline. Empty for now, and saves wait, so the file on disk can't be replaced by nothing.
            Document = FreehandDocument.Empty;
            _readFailed = true;
        }
    }

    /// <summary>Shows a canvas, saving the current one first.</summary>
    public void Open(string name)
    {
        if (name == CurrentName || !Flush()) return;
        CancelDrag();
        RingAt = null;
        Tool = FreehandTool.None;
        SelectedId = null;
        CropId = null;
        CurrentName = name;
        _files = new FreehandFiles(canvases.PathOf(name));
        LoadCurrent();
        Remember(name);
        RefreshList();
    }

    public void NewCanvas() => Open(canvases.Create(null));

    public void RenameCurrent(string name)
    {
        if (!Flush()) throw new LibraryUnavailableException();
        var chosen = canvases.Rename(CurrentName, name);
        CurrentName = chosen;
        _files = new FreehandFiles(canvases.PathOf(chosen));
        Remember(chosen);
        RefreshList();
    }

    public void DuplicateCurrent()
    {
        if (!Flush()) throw new LibraryUnavailableException();
        Open(canvases.Duplicate(CurrentName));
    }

    /// <summary>Moves the current canvas to the Recycle Bin, then opens another. A library always has one.</summary>
    public void DeleteCurrent()
    {
        host.MoveToRecycleBin([canvases.PathOf(CurrentName)]);
        // It's gone, so nothing is left to save. A name it can't reopen forces the load below.
        _isDirty = false;
        _saveFailed = false;
        _readFailed = false;
        _saveTimer?.Dispose();
        CurrentName = "";
        Open(canvases.EnsureOne());
    }

    public void LoadIfNeeded()
    {
        if (_isLoaded) return;
        _isLoaded = true;
        settings.Current.LastCanvas.TryGetValue(libraryId.ToString(), out var remembered);
        var list = canvases.List();
        string name;
        if (remembered != null && list.Contains(remembered)) name = remembered;
        else if (list.Count > 0) name = list[0];
        else
        {
            try { name = canvases.EnsureOne(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { name = "Untitled"; }
        }
        Open(name);
    }

    // Pointer

    public void MouseDown(Point2 point, int clickCount)
    {
        RingAt = null;
        _dragLast = point;
        _dragStart = point;
        _dragMoved = false;
        NotifyChanged();
        if (HandleDrag(point) is { } grabbed)
        {
            _drag = grabbed;
            return;
        }
        if (FreehandGeometry.Hit(Document.Items, Document.Camera.ToCanvas(point)) is not { } id)
        {
            _drag = new PanDrag();
            return;
        }
        if (SelectedId != id) Tool = FreehandTool.None;
        SelectedId = id;
        if (Document.Items[^1].Id != id)
        {
            Document = Document with { Items = FreehandGeometry.BroughtToFront(Document.Items, id) };
            ScheduleSave();
        }
        if (clickCount == 2) RingAt = point;
        _drag = new ItemDrag(id);
    }

    public void MouseDragged(Point2 point, bool shift)
    {
        if (_drag == null) return;
        if (!_dragMoved)
        {
            if (Math.Sqrt(Math.Pow(point.X - _dragStart.X, 2) + Math.Pow(point.Y - _dragStart.Y, 2)) < DragThreshold) return;
            _dragMoved = true;
            RingAt = null;
        }
        var delta = new Size2(point.X - _dragLast.X, point.Y - _dragLast.Y);
        _dragLast = point;
        switch (_drag)
        {
            case PanDrag:
                SetCamera(Document.Camera.Panned(delta));
                break;
            case ResizeDrag r:
                var pointer = Document.Camera.ToCanvas(new Point2(point.X - r.Grab.Width, point.Y - r.Grab.Height));
                Replace(r.Id, i => FreehandGeometry.Resized(i, r.Corner, pointer));
                break;
            case RotateDrag t:
                Replace(t.Id, i => FreehandGeometry.Rotated(i, Document.Camera.ToCanvas(point), shift));
                break;
            case ItemDrag d:
                var zoom = Document.Camera.Zoom;
                Replace(d.Id, i => i with { Center = new Point2(i.Center.X + delta.Width / zoom, i.Center.Y + delta.Height / zoom) });
                break;
        }
    }

    void Replace(Guid id, Func<FreehandItem, FreehandItem> change)
    {
        Document = Document with { Items = Document.Items.Select(i => i.Id == id ? change(i) : i).ToList() };
        ScheduleSave();
    }

    public void MouseUp()
    {
        if (_drag is PanDrag && !_dragMoved)
        {
            SelectedId = null;
            Tool = FreehandTool.None;
        }
        _drag = null;
        NotifyChanged();
    }

    /// <summary>The view went away: the store outlives it, so nothing may be left open for the next visit.</summary>
    public void Leave()
    {
        CancelDrag();
        RingAt = null;
        Tool = FreehandTool.None;
        CropId = null;
        Flush();
    }

    /// <summary>Ends a drag whose mouse up will never arrive, because the view went away.</summary>
    public void CancelDrag() => _drag = null;

    // Tools

    /// <summary>The handle under a press, if a tool is active: corners for resize, the rotate handle for rotate.</summary>
    Drag? HandleDrag(Point2 point)
    {
        if (Tool == FreehandTool.None || Document.Items.FirstOrDefault(i => i.Id == SelectedId) is not { } item) return null;
        var camera = Document.Camera;
        const double reach = HandleHitSize / 2;
        Size2? Grab(Point2 target) => Math.Abs(point.X - target.X) <= reach && Math.Abs(point.Y - target.Y) <= reach
            ? new Size2(point.X - target.X, point.Y - target.Y) : null;
        if (Tool == FreehandTool.Resize)
        {
            var corners = FreehandGeometry.Corners(item);
            foreach (var corner in Enum.GetValues<Corner>())
                if (Grab(camera.ToScreen(corners[(int)corner])) is { } offset) return new ResizeDrag(item.Id, corner, offset);
            return null;
        }
        return Grab(camera.ToScreen(FreehandGeometry.RotateHandle(item, camera.Zoom))) == null ? null : new RotateDrag(item.Id);
    }

    /// <summary>A choice from the circular menu. It closes the menu.</summary>
    public void ChooseTool(FreehandTool chosen)
    {
        RingAt = null;
        Tool = chosen;
        NotifyChanged();
    }

    public void FlipSelected()
    {
        RingAt = null;
        if (SelectedId is { } id) Replace(id, FreehandGeometry.Flipped);
        NotifyChanged();
    }

    public void BeginCrop()
    {
        RingAt = null;
        Tool = FreehandTool.None;
        CropId = SelectedId;
        NotifyChanged();
    }

    /// <summary>Applies the crop from the window. The kept part stays where it was, so nothing else moves.</summary>
    public void ApplyCrop(NormalizedRect crop)
    {
        if (CropId is not { } id) return;
        var clamped = CropMath.ClampedToUnit(crop);
        if (clamped.Width <= 0 || clamped.Height <= 0) return;
        Replace(id, i => FreehandGeometry.Cropped(i, clamped));
    }

    public void EndCrop()
    {
        CropId = null;
        NotifyChanged();
    }

    // Camera

    public void Scroll(Size2 delta) => SetCamera(Document.Camera.Panned(delta));

    public void Magnify(double factor, Point2 point)
    {
        if (factor > 0) SetCamera(Document.Camera.Zoomed(factor, point));
    }

    public void ZoomStep(bool zoomingIn) =>
        Magnify(zoomingIn ? ZoomStepFactor : 1 / ZoomStepFactor, new Point2(Viewport.Width / 2, Viewport.Height / 2));

    public void Fit() => SetCamera(FreehandGeometry.Fitted(Document.Items, Viewport));

    void SetCamera(FreehandCamera camera)
    {
        if (camera == Document.Camera) return;
        Document = Document with { Camera = camera };
        RingAt = null;
        ScheduleSave();
    }

    // Edits

    public void SetGreyscale(bool on)
    {
        if (on == Document.Greyscale) return;
        Document = Document with { Greyscale = on };
        ScheduleSave();
    }

    public void DeleteSelected()
    {
        if (SelectedId is { } id) Remove(id);
    }

    /// <summary>Removes one image and its canvas copy. Everything that removes a single image comes through here.</summary>
    public void Remove(Guid id) => RemoveItems(i => i.Id == id);

    public void Clear() => RemoveItems(_ => true);

    /// <summary>Closes the circular menu, otherwise the tool, otherwise the selection.</summary>
    public void Escape()
    {
        if (RingAt != null) RingAt = null;
        else if (Tool != FreehandTool.None) Tool = FreehandTool.None;
        else SelectedId = null;
        NotifyChanged();
    }

    /// <summary>The file goes only after a save no longer refers to it. If the save fails the file stays,
    /// and the orphan cleanup takes it once a later save has succeeded.</summary>
    void RemoveItems(Func<FreehandItem, bool> shouldRemove)
    {
        var removed = Document.Items.Where(shouldRemove).ToList();
        if (removed.Count == 0) return;
        Document = Document with { Items = Document.Items.Where(i => !shouldRemove(i)).ToList() };
        if (removed.Any(i => i.Id == SelectedId))
        {
            SelectedId = null;
            Tool = FreehandTool.None;
            RingAt = null;
        }
        _isDirty = true;
        if (SaveNow())
            foreach (var item in removed) _files.RemoveImage(Path.GetFileName(_files.ImagePath(item.FileName)));
        NotifyChanged();
    }

    // Saving

    /// <summary>Returns false if the canvas couldn't be saved.</summary>
    public bool Flush() => !_isDirty || SaveNow();

    void ScheduleSave()
    {
        _isDirty = true;
        _saveTimer?.Dispose();
        _saveTimer = host.After(SaveDelay, () => Flush());
        NotifyChanged();
    }

    bool SaveNow()
    {
        _saveTimer?.Dispose();
        if (_readFailed) return false;
        try
        {
            _files.Save(Document);
            _isDirty = false;
            _saveFailed = false;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _saveFailed = true;
            NotifyChanged();
            return false;
        }
    }

    // Importing

    /// <summary>Copies each readable image in, a few at a time and off the UI thread, then places them centered on the point,
    /// in the order given. The last one ends up on top and selected.</summary>
    public async Task ImportFilesAsync(IReadOnlyList<string> paths, Point2 point)
    {
        var files = _files;
        var startedIn = CurrentName;
        List<string> taken;
        try
        {
            files.PrepareImagesDir();
            taken = Directory.EnumerateFileSystemEntries(files.ImagesDir).Select(p => Path.GetFileName(p)!).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            NoteSkipped(paths.Count);
            return;
        }
        // Names are settled here, so two files can't claim one.
        var targets = new List<string>();
        foreach (var path in paths)
        {
            var name = PhotoScan.UniqueName(Path.GetFileName(path), taken);
            taken.Add(name);
            targets.Add(name);
        }
        using var gate = new SemaphoreSlim(MaxConcurrentImports);
        var copied = await Task.WhenAll(paths.Select((path, i) => Task.Run(async () =>
        {
            await gate.WaitAsync();
            try { return CopyIn(path, targets[i], files, host); }
            finally { gate.Release(); }
        })));
        var done = copied.OfType<Copied>().ToList();
        // The canvas changed while copying. Its document is no longer the one these belong to.
        if (CurrentName != startedIn)
        {
            foreach (var copy in done) files.RemoveImage(copy.FileName);
            return;
        }
        Place(done, point);
        NoteSkipped(paths.Count - done.Count);
    }

    void Place(IReadOnlyList<Copied> images, Point2 point)
    {
        var camera = Document.Camera;
        Guid? last = null;
        var items = Document.Items.ToList();
        for (var step = 0; step < images.Count; step++)
        {
            var image = images[step];
            var offset = step * DropStagger;
            var scale = NewImageLongEdge / camera.Zoom / Math.Max(image.Width, image.Height);
            var item = new FreehandItem(Guid.NewGuid(), image.FileName, camera.ToCanvas(new Point2(point.X + offset, point.Y + offset)), new Size2(image.Width * scale, image.Height * scale));
            items.Add(item);
            last = item.Id;
        }
        if (last == null) return;
        Document = Document with { Items = items };
        SelectedId = last;
        Tool = FreehandTool.None;
        RingAt = null;
        ScheduleSave();
    }

    void NoteSkipped(int count)
    {
        if (count <= 0) return;
        _skipNote = count == 1 ? "Skipped 1 file" : $"Skipped {count} files";
        _skipTimer?.Dispose();
        _skipTimer = host.After(SkipNoteDuration, () =>
        {
            _skipNote = null;
            NotifyChanged();
        });
        NotifyChanged();
    }

    /// <summary>Reads only the file's header for its size, so a very large image isn't decoded here.</summary>
    static Copied? CopyIn(string path, string fileName, FreehandFiles files, ILibraryHost host)
    {
        try
        {
            if (!host.IsReadableImage(path) || host.OrientedPixelSize(path) is not { } size) return null;
            File.Copy(path, Path.Combine(files.ImagesDir, fileName));
            return new Copied(fileName, size.Width, size.Height);
        }
        catch (Exception)
        {
            // One bad file, whatever way the imaging system failed on it, never costs the others.
            return null;
        }
    }
}

/// <summary>Where the circular menu's five buttons go: on a 64pt radius, the first straight up.</summary>
public static class FreehandRing
{
    public const double Radius = 64, ButtonSize = 36, DiscSize = ButtonSize + 6, BarReservedHeight = 66;

    /// <summary>Button centers relative to the ring's center, evenly spaced, the first straight up.</summary>
    public static IReadOnlyList<Point2> Offsets() => Enumerable.Range(0, 5).Select(i =>
    {
        var angle = (-90 + 360.0 * i / 5) * Math.PI / 180;
        return new Point2(Radius * Math.Cos(angle), Radius * Math.Sin(angle));
    }).ToList();

    /// <summary>The point, moved inward just far enough that every disc stays inside the viewport, and above the floating
    /// bar, which is barWidth wide at the bottom center, if the ring would reach it sideways.</summary>
    public static Point2 Center(Point2 point, Size2 viewport, double barWidth)
    {
        const double reach = DiscSize / 2;
        var all = Offsets();
        double lowX = all.Min(p => p.X) - reach, lowY = all.Min(p => p.Y) - reach, highX = all.Max(p => p.X) + reach, highY = all.Max(p => p.Y) + reach;
        static double Fit(double value, double low, double high, double extent)
        {
            double lower = -low, upper = extent - high;
            return lower > upper ? extent / 2 : Math.Min(Math.Max(value, lower), upper);
        }
        var x = Fit(point.X, lowX, highX, viewport.Width);
        var overBar = barWidth > 0 && x + lowX < (viewport.Width + barWidth) / 2 && x + highX > (viewport.Width - barWidth) / 2;
        var floor = viewport.Height - (overBar ? BarReservedHeight : 0);
        return new Point2(x, Fit(point.Y, lowY, highY, floor));
    }
}

/// <summary>The small notes from s.e.a. over Freehand, for one visit. now is seconds on a clock that doesn't count sleep.</summary>
public sealed class FreehandNotes(string name, Random rng)
{
    readonly Linework.Core.Copy.SeaNoteState _state = new();
    bool _active;
    double _start, _nextAt, _hideAt;

    public string? Shown { get; private set; }
    public double? ChosenAtVisitSeconds { get; private set; }

    /// <summary>Sleep ends the visit and hides the note; the next update after waking starts a new one.</summary>
    public void Sleep()
    {
        Shown = null;
        _active = false;
    }

    /// <summary>busy is a drag or the circular menu: a due note waits. True if a note just appeared.</summary>
    public bool Update(double now, bool busy, bool enabled, int? usualSeconds)
    {
        if (!enabled)
        {
            Sleep();
            return false;
        }
        if (!_active)
        {
            _active = true;
            _start = now;
            _nextAt = now + Linework.Core.Copy.SeaNotes.FirstDelay;
        }
        if (Shown != null && now >= _hideAt)
        {
            Shown = null;
            _nextAt = now + Linework.Core.Copy.SeaNotes.NextInterval(rng);
        }
        if (Shown != null || now < _nextAt || busy) return false;
        ChosenAtVisitSeconds = now - _start;
        Shown = Linework.Core.Copy.SeaNotes.Next((int)(now - _start), name, usualSeconds, _state, rng);
        _hideAt = now + Linework.Core.Copy.SeaNotes.VisibleSeconds;
        return true;
    }
}
