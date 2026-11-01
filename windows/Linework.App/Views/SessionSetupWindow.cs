using System.Windows;
using System.Windows.Controls;
using Linework.App.Library;
using Linework.App.Theme;
using Linework.Core.Library;
using Linework.Core.Session;

namespace Linework.App.Views;

public abstract record SessionSource;
/// <summary>Images imported today first, the rest of the session filled from the library, sized to a target length.</summary>
public sealed record SourceToday : SessionSource;
public sealed record SourceAll : SessionSource;
public sealed record SourceBenchmarks : SessionSource;
public sealed record SourceSet(Guid Id) : SessionSource;

public sealed class SessionSetupWindow : GlassWindow
{
    static readonly int[] PresetSeconds = [30, 60, 120, 300];
    static readonly int[] StepDurations = [15, 30, 45, 60, 90, 120, 180, 300, 600, 900, 1200];

    readonly LineworkContext _context;
    readonly LibraryStore _library;
    readonly IReadOnlyList<PhotoRecord> _images;
    readonly ContentControl _holder = new();
    SessionSource _source;
    Guid? _presetId;
    List<ClassStep> _steps;

    Settings Settings => _context.Settings;
    Settings.Values V => Settings.Current;

    public SessionSetupWindow(LineworkContext context, LibraryStore library) : base(context.Settings)
    {
        _context = context;
        _library = library;
        // Sessions skip what Windows can't read.
        _images = ReadablePhotos.Filter(library, context.App.Host).OrderBy(p => p.ImportedAt).ToList();
        _source = V.SetupToday ? new SourceToday() : new SourceAll();
        var presets = Presets;
        _presetId = presets.FirstOrDefault()?.Id;
        _steps = presets.FirstOrDefault()?.Steps.ToList() ?? [.. QueueBuilder.DefaultClass];
        Title = "New session";
        Width = 520;
        Height = 680;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Content = _holder;
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) Close();
        };
        Draw();
    }

    public FrameworkElement ContentForShot => _holder;

    List<PresetRecord> Presets => _library.Presets.OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();

    void Draw()
    {
        var due = _library.DueBenchmarks();
        var todayCount = TodayIds.Count;
        var preview = Build(due, false);
        var body = Ui.Column(18);
        Ui.Add(body, SourcePicker(todayCount), 18);
        var modes = Ui.Row(6, Ui.Capsule("Simple", () => Set(v => v with { SetupClassMode = false }), 30, !V.SetupClassMode),
            Ui.Capsule("Class", () => Set(v => v with { SetupClassMode = true }), 30, V.SetupClassMode));
        Ui.Add(body, modes, 18);
        Ui.Add(body, Ui.Rule(), 18);
        if (_source is SourceToday)
        {
            Ui.Add(body, TodaySection(todayCount), 18);
            Ui.Add(body, Ui.Rule(), 18);
        }
        Ui.Add(body, V.SetupClassMode ? ClassSection() : SimpleSection(), 18);
        Ui.Add(body, Ui.Rule(), 18);
        Ui.Add(body, Ui.Row(6,
            Ui.CapsuleToggle("Shuffle", V.SetupShuffle, on => Set(v => v with { SetupShuffle = on })),
            Ui.CapsuleToggle("Sound cues", V.Sound, on => Set(v => v with { Sound = on })),
            Ui.CapsuleToggle("Start in grayscale", V.SetupGrayscale, on => Set(v => v with { SetupGrayscale = on }))), 18);
        if (due.Count > 0)
            Ui.Add(body, Ui.Text($"{due.Count} benchmark{(due.Count == 1 ? "" : "s")} due — {(due.Count == 1 ? "it" : "they")}’ll play first.", 14, Ui.Medium), 18);
        body.Margin = new Thickness(28, 0, 28, 16);

        var start = Ui.Capsule("Start", () => Start(due), 30, true);
        start.IsEnabled = preview.Poses.Count > 0;
        var end = Ui.Row(8, Ui.Capsule("Cancel", Close, 30), start).Align(HorizontalAlignment.Right, VerticalAlignment.Center);
        var summary = Ui.Text(Summary(preview), 13, null, Tokens.Muted, wrap: true).Align(HorizontalAlignment.Left, VerticalAlignment.Center);
        var footer = new DockPanel { Margin = new Thickness(28, 16, 28, 16) };
        DockPanel.SetDock(end, Dock.Right);
        footer.Children.Add(end);
        footer.Children.Add(summary);

        var layout = new DockPanel();
        var title = Ui.Text("New session", 26, Ui.Medium).Margin(28, 28, 28, 16);
        DockPanel.SetDock(title, Dock.Top);
        layout.Children.Add(title);
        var bottom = Ui.Column(0, Ui.Rule(), footer);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(bottom);
        layout.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body, Focusable = false });
        _holder.Content = layout;
    }

    void Set(Func<Settings.Values, Settings.Values> change)
    {
        Settings.Update(change);
        Draw();
    }

    FrameworkElement SourcePicker(int todayCount)
    {
        var box = new ComboBox { FontFamily = Tokens.Font, FontSize = 14 };
        void Add(string text, SessionSource source)
        {
            var item = new ComboBoxItem { Content = text, Tag = source };
            box.Items.Add(item);
            if (source == _source) box.SelectedItem = item;
        }
        Add($"Today’s Images ({todayCount})", new SourceToday());
        box.Items.Add(new Separator());
        Add($"All Images ({_images.Count})", new SourceAll());
        if (FeatureFlags.BenchmarksAndFreehand) Add($"Benchmarks ({_images.Count(i => _library.IsBenchmark(i.Id))})", new SourceBenchmarks());
        foreach (var set in _library.Sets) Add($"{set.Name} ({SetImages(set).Count})", new SourceSet(set.Id));
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is not ComboBoxItem { Tag: SessionSource source } || source == _source) return;
            _source = source;
            Settings.Update(v => v with { SetupToday = source is SourceToday });
            Dispatcher.BeginInvoke(Draw);
        };
        var dock = new DockPanel();
        var label = Ui.Text("Images").Align(HorizontalAlignment.Left, VerticalAlignment.Center).Margin(0, 0, 12);
        DockPanel.SetDock(label, Dock.Left);
        dock.Children.Add(label);
        dock.Children.Add(box);
        return dock;
    }

    FrameworkElement SimpleSection()
    {
        var panel = Ui.Column(12, Ui.Text("Time per pose", 12, null, Tokens.Muted));
        var presets = Ui.Row(6);
        foreach (var s in PresetSeconds)
            Ui.Add(presets, Ui.Capsule(DurationFormat.Short(s), () => Set(v => v with { SetupSeconds = s }), 30, V.SetupSeconds == s), 6);
        Ui.Add(panel, presets, 12);
        var seconds = V.SetupSeconds;
        var step = seconds < 60 ? 5 : 15;
        Ui.Add(panel, Ui.Stepper($"Custom: {DurationFormat.Short(seconds)}",
            () => Set(v => v with { SetupSeconds = Math.Max(seconds - step, 5) }),
            () => Set(v => v with { SetupSeconds = Math.Min(seconds + step, 3600) })), 12);
        if (_source is not SourceToday)
        {
            var max = V.SetupMaxPoses;
            Ui.Add(panel, Ui.Stepper(max == 0 ? "Poses: all images" : $"Poses: {max}",
                () => Set(v => v with { SetupMaxPoses = Math.Max(max - 1, 0) }),
                () => Set(v => v with { SetupMaxPoses = Math.Min(max + 1, 500) })), 12);
        }
        return panel;
    }

    FrameworkElement TodaySection(int todayCount)
    {
        var minutes = V.SetupTargetMinutes;
        var text = todayCount == 0
            ? "No images imported today yet — the session uses random images from your library."
            : $"Today’s {todayCount} {(todayCount == 1 ? "image plays" : "images play")} first, then random images from your library fill the rest.";
        return Ui.Column(8,
            Ui.Stepper($"Session length: {minutes} min",
                () => Set(v => v with { SetupTargetMinutes = Math.Max(minutes - 5, 5) }),
                () => Set(v => v with { SetupTargetMinutes = Math.Min(minutes + 5, 180) })),
            Ui.Text(text, 13, null, Tokens.Muted, wrap: true));
    }

    FrameworkElement ClassSection()
    {
        var panel = Ui.Column(12, Ui.Text("Class steps", 12, null, Tokens.Muted));
        var presets = Presets;
        var box = new ComboBox { FontFamily = Tokens.Font, FontSize = 14, DisplayMemberPath = "Name", ItemsSource = presets, SelectedItem = presets.FirstOrDefault(p => p.Id == _presetId) };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is not PresetRecord preset || preset.Id == _presetId) return;
            _presetId = preset.Id;
            _steps = preset.Steps.ToList();
            Dispatcher.BeginInvoke(Draw);
        };
        var pick = new DockPanel();
        var label = Ui.Text("Preset").Align(HorizontalAlignment.Left, VerticalAlignment.Center).Margin(0, 0, 12);
        DockPanel.SetDock(label, Dock.Left);
        pick.Children.Add(label);
        pick.Children.Add(box);
        Ui.Add(panel, pick, 12);
        for (var i = 0; i < _steps.Count; i++) Ui.Add(panel, StepRow(i), 6);
        var buttons = new DockPanel();
        var add = Ui.Capsule("Add step", () =>
        {
            _steps.Add(new ClassStep(2, 120));
            Draw();
        }, 30);
        DockPanel.SetDock(add, Dock.Left);
        buttons.Children.Add(add);
        buttons.Children.Add(Ui.Capsule("Save as preset…", SavePreset, 30).Align(HorizontalAlignment.Right));
        Ui.Add(panel, buttons, 12);
        return panel;
    }

    FrameworkElement StepRow(int i)
    {
        var step = _steps[i];
        void Replace(ClassStep value)
        {
            _steps[i] = value;
            Draw();
        }
        var duration = new ComboBox { FontFamily = Tokens.Font, FontSize = 14, Width = 100, ItemsSource = StepDurations.Select(DurationFormat.Short).ToList(), SelectedItem = DurationFormat.Short(step.Seconds) };
        duration.SelectionChanged += (_, _) =>
        {
            var index = duration.SelectedIndex;
            if (index >= 0 && StepDurations[index] != step.Seconds) Dispatcher.BeginInvoke(() => Replace(step with { Seconds = StepDurations[index] }));
        };
        var row = Ui.Row(6,
            Ui.Text($"{step.Count} ×").Align(HorizontalAlignment.Left, VerticalAlignment.Center),
            Ui.Capsule("−", () => Replace(step with { Count = Math.Max(step.Count - 1, 1) }), 26),
            Ui.Capsule("+", () => Replace(step with { Count = Math.Min(step.Count + 1, 100) }), 26),
            duration);
        var tools = Ui.Row(4);
        Button Tool(string title, Action action, bool enabled, string help)
        {
            var b = Ui.Capsule(title, action, 26);
            b.IsEnabled = enabled;
            b.ToolTip = help;
            return b;
        }
        Ui.Add(tools, Tool("↑", () => Swap(i, i - 1), i > 0, "Move up"), 4);
        Ui.Add(tools, Tool("↓", () => Swap(i, i + 1), i < _steps.Count - 1, "Move down"), 4);
        Ui.Add(tools, Tool("×", () =>
        {
            _steps.RemoveAt(i);
            Draw();
        }, _steps.Count > 1, "Remove"), 4);
        var dock = new DockPanel();
        DockPanel.SetDock(tools, Dock.Right);
        dock.Children.Add(tools);
        dock.Children.Add(row);
        return dock;
    }

    void Swap(int a, int b)
    {
        (_steps[a], _steps[b]) = (_steps[b], _steps[a]);
        Draw();
    }

    void SavePreset()
    {
        var name = AlertWindow.AskText("Save class preset", "")?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        _presetId = _library.AddPreset(name, _steps.ToList()).Id;
        Draw();
    }

    List<PhotoRecord> SetImages(SetRecord set) => _images.Where(i => set.PhotoIDs.Contains(i.Id)).ToList();

    List<Guid> TodayIds => _images.Where(i => DateOnly.FromDateTime(i.ImportedAt.ToLocalTime().DateTime) == _context.Session.Today).Select(i => i.Id).ToList();

    List<Guid> SourceIds => _source switch
    {
        SourceToday => TodayIds,
        SourceBenchmarks => _images.Where(i => _library.IsBenchmark(i.Id)).Select(i => i.Id).ToList(),
        SourceSet s => _library.Sets.FirstOrDefault(x => x.Id == s.Id) is { } set ? SetImages(set).Select(i => i.Id).ToList() : [],
        _ => _images.Select(i => i.Id).ToList(),
    };

    /// <summary>The rest of the library, used to fill a Today session.</summary>
    List<Guid> FillIds
    {
        get
        {
            if (_source is not SourceToday) return [];
            var today = TodayIds.ToHashSet();
            return _images.Select(i => i.Id).Where(i => !today.Contains(i)).ToList();
        }
    }

    /// <summary>For Today, poses (simple) or step counts (class) are sized so the session, benchmarks included, lasts about the target.</summary>
    SessionMode Mode(IReadOnlyList<DueBenchmark> due)
    {
        if (_source is not SourceToday)
            return V.SetupClassMode ? new ClassStepsMode(_steps) : new SimpleMode(V.SetupSeconds, V.SetupMaxPoses == 0 ? null : V.SetupMaxPoses);
        var target = Math.Max(V.SetupTargetMinutes * 60 - due.Sum(d => d.Seconds), 60);
        return V.SetupClassMode
            ? new ClassStepsMode(SessionLength.Scaled(_steps, target))
            : new SimpleMode(V.SetupSeconds, SessionLength.Poses(target, V.SetupSeconds));
    }

    SessionQueue Build(IReadOnlyList<DueBenchmark> due, bool shuffle) => QueueBuilder.Build(SourceIds, FillIds, Mode(due), shuffle, due, Random.Shared);

    static string Summary(SessionQueue q)
    {
        if (q.Poses.Count == 0) return "No images in this source — drag some photos in first.";
        var text = $"{q.Poses.Count} poses · {DurationFormat.Total(q.Poses.Sum(p => p.Seconds))}";
        return q.RepeatedImages ? text + " · images will repeat" : text;
    }

    void Start(IReadOnlyList<DueBenchmark> due)
    {
        var queue = Build(due, V.SetupShuffle);
        if (queue.Poses.Count == 0) return;
        var session = _context.Session;
        session.Queue = queue;
        session.PlanBlock = null;
        session.SoundOn = V.Sound;
        session.StartGrayscale = V.SetupGrayscale;
        var owner = Owner;
        Close();
        SessionPlayerWindow.Open(_context, _library, owner);
    }
}

/// <summary>A format needs Windows to have its codec, and most never need checking. Asks once per kind of file.</summary>
public static class ReadablePhotos
{
    static readonly HashSet<string> AlwaysThere = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff"];

    public static IReadOnlyList<PhotoRecord> Filter(LibraryStore store, ILibraryHost host)
    {
        var known = new Dictionary<string, bool>();
        return store.VisiblePhotos.Where(photo =>
        {
            var extension = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (AlwaysThere.Contains(extension)) return true;
            if (!known.TryGetValue(extension, out var readable)) known[extension] = readable = host.IsReadableImage(store.PhotoPath(photo));
            return readable;
        }).ToList();
    }
}
