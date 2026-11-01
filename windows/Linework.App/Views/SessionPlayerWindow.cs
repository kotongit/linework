using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Linework.App.Library;
using Linework.App.Platform;
using Linework.App.Theme;
using Linework.Core.Copy;
using Linework.Core.Library;
using Linework.Core.Plan;
using Linework.Core.Session;
using Microsoft.Win32;

namespace Linework.App.Views;

/// <summary>Borderless over the whole monitor the main window is on. Black, whatever the system theme.</summary>
public sealed class SessionPlayerWindow : Window
{
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

    [StructLayout(LayoutKind.Sequential)] public struct Rect32 { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfo
    {
        public int Size;
        public Rect32 Monitor, Work;
        public uint Flags;
    }

    const uint MonitorDefaultToNearest = 2, SwpNoZOrder = 4, SwpNoActivate = 0x10;
    static readonly Brush Capsule = new SolidColorBrush(Color.FromArgb(0x77, 0x20, 0x20, 0x20));

    readonly LineworkContext _context;
    readonly LibraryStore _library;
    readonly SessionController _session;
    readonly PracticeNotes _notes;
    readonly Window? _anchor;
    readonly Dictionary<int, BitmapSource> _rendered = [];
    readonly Image _image = new() { Stretch = Stretch.Uniform, Margin = new Thickness(24), UseLayoutRounding = true, SnapsToDevicePixels = true };
    readonly TextBlock _timer = Ui.Text("", 28, Ui.Medium, Brushes.White, tabular: true);
    readonly Border _timerBox = new() { Background = Capsule, CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 4, 16, 4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
    readonly Border _benchmarkBox = new() { Background = Capsule, CornerRadius = new CornerRadius(17), BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Padding = new Thickness(14, 8, 14, 8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    readonly TextBlock _benchmark = Ui.Text("", 12, Ui.Medium, Brushes.White);
    readonly Border _pausedBox, _hintsBox, _countBox;
    readonly TextBlock _count = Ui.Text("", 12, Ui.Medium, Brushes.White, tabular: true);
    readonly StackPanel _getReady = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _getReadyNumber = Ui.Text("", 160, Ui.Medium, Brushes.White, tabular: true);
    readonly TextBlock _getReadyNext = Ui.Text("", 20, Ui.Medium, Brushes.White);
    readonly Grid _progressHolder = new() { Height = 3, VerticalAlignment = VerticalAlignment.Bottom };
    readonly Border _progress = new() { Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
    readonly ContentControl _noteHolder = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 0, 0), IsHitTestVisible = false };
    readonly Grid _running = new();
    readonly ContentControl _page = new();
    readonly DispatcherTimer _timerTick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _noteClock;
    TimeSpan _last;
    bool _grayscale, _showTimer = true, _finishedShown;
    string? _shownNote;
    int _shownIndex = -1;
    bool _shownGray;

    public SessionController Session => _session;
    public int ScreenPixels { get; private set; } = 3000;
    public Rect32 MonitorRect { get; private set; }
    public Image PoseImage => _image;

    public static SessionPlayerWindow Open(LineworkContext context, LibraryStore library, Window? anchor, bool show = true)
    {
        var window = new SessionPlayerWindow(context, library, anchor);
        if (show) window.Show();
        return window;
    }

    SessionPlayerWindow(LineworkContext context, LibraryStore library, Window? anchor)
    {
        _context = context;
        _library = library;
        _anchor = anchor;
        _session = new SessionController(library, context.Session, context.App.Host, new WpfSessionEffects());
        _session.AttemptNeeded += AskAttempt;
        _notes = new PracticeNotes(context.Settings.Current.Name, Random.Shared);
        _grayscale = context.Session.StartGrayscale;
        Title = "Session";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Black;
        ShowInTaskbar = true;
        Owner = anchor;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        _pausedBox = Box(Ui.Text("Paused — press Space", 15, Ui.Medium, Brushes.White), 16, 8);
        _hintsBox = Box(Ui.Text("Space pause · ← → move · G grayscale · T timer · Esc end", 13, null, Brushes.White), 14, 6);
        _countBox = Box(_count, 10, 4);
        _timerBox.Child = _timer;
        _benchmarkBox.Child = _benchmark;

        _running.Children.Add(_image);
        var top = new Grid { Margin = new Thickness(20) };
        top.Children.Add(_benchmarkBox);
        top.Children.Add(_timerBox);
        _running.Children.Add(top);
        var bottom = Ui.Column(8, _pausedBox, _hintsBox, _countBox).Align(HorizontalAlignment.Center, VerticalAlignment.Bottom).Margin(0, 0, 0, 20);
        foreach (FrameworkElement e in bottom.Children) e.HorizontalAlignment = HorizontalAlignment.Center;
        _running.Children.Add(bottom);
        _running.Children.Add(_noteHolder);
        _getReady.Children.Add(_getReadyNumber);
        _getReadyNumber.HorizontalAlignment = HorizontalAlignment.Center;
        _getReady.Children.Add(_getReadyNext.Margin(0, 12));
        _getReadyNext.HorizontalAlignment = HorizontalAlignment.Center;
        _running.Children.Add(_getReady);
        _progressHolder.Children.Add(_progress);
        _running.Children.Add(_progressHolder);
        _page.Content = _running;
        Content = _page;

        SourceInitialized += (_, _) => CoverMonitor();
        DpiChanged += (_, _) => CoverMonitor();
        PreviewKeyDown += OnKey;
        _timerTick.Tick += (_, _) => Tick();
        Closed += OnClosed;
        Loaded += (_, _) =>
        {
            Focus();
            Prefetch(0);
            _last = _clock.Elapsed;
            _timerTick.Start();
            var hints = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            hints.Tick += (_, _) =>
            {
                hints.Stop();
                _hintsBox.Visibility = Visibility.Collapsed;
            };
            hints.Start();
        };
        SystemEvents.PowerModeChanged += OnPower;
        Update();
    }

    Border Box(UIElement child, double h, double v) => new()
    {
        Background = Capsule,
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(h, v, h, v),
        Child = child,
    };

    /// <summary>Works in device pixels, so it holds at any scale and on any monitor.</summary>
    void CoverMonitor()
    {
        var anchor = _anchor != null ? new WindowInteropHelper(_anchor).Handle : IntPtr.Zero;
        var hwnd = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(anchor != IntPtr.Zero ? anchor : hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        var m = info.Monitor;
        MonitorRect = m;
        ScreenPixels = Math.Max(m.Right - m.Left, m.Bottom - m.Top);
        SetWindowPos(hwnd, IntPtr.Zero, m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top, SwpNoZOrder | SwpNoActivate);
    }

    void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        // Time asleep is never counted.
        if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(() => _last = _clock.Elapsed);
    }

    void Tick()
    {
        var now = _clock.Elapsed;
        var dt = (now - _last).TotalSeconds;
        _last = now;
        // A gap this long means the machine slept before the resume message came; it doesn't use up the pose.
        if (dt > 5) return;
        _noteClock += dt;
        var events = _session.Advance(dt);
        if (events.HasFlag(RunnerEvent.PoseChanged)) Prefetch(_session.Runner.Index);
        Update();
    }

    void Update()
    {
        var runner = _session.Runner;
        if (runner.Phase == RunnerPhase.Finished)
        {
            ShowSummary();
            return;
        }
        var pose = runner.Current;
        var getReady = runner.Phase == RunnerPhase.GetReady;
        ShowImage(runner.Index);
        _image.Opacity = getReady ? 0.25 : 1;
        _timerBox.Visibility = _showTimer ? Visibility.Visible : Visibility.Collapsed;
        _timer.Text = DurationFormat.Clock(runner.Remaining);
        _benchmarkBox.Visibility = pose?.BenchmarkNumber != null ? Visibility.Visible : Visibility.Collapsed;
        if (pose?.BenchmarkNumber is { } number) _benchmark.Text = $"BENCHMARK #{number.ToString(CultureInfo.InvariantCulture)}";
        _pausedBox.Visibility = runner.IsPaused ? Visibility.Visible : Visibility.Collapsed;
        _count.Text = $"{(runner.Index + 1).ToString(CultureInfo.InvariantCulture)} / {runner.Poses.Count.ToString(CultureInfo.InvariantCulture)}";
        _getReady.Visibility = getReady ? Visibility.Visible : Visibility.Collapsed;
        if (getReady)
        {
            _getReadyNumber.Text = ((int)Math.Ceiling(runner.GetReadyRemaining)).ToString(CultureInfo.InvariantCulture);
            if (pose != null) _getReadyNext.Text = pose.BenchmarkNumber != null ? $"Benchmark: {DurationFormat.Short(pose.Seconds)}" : $"Next: {DurationFormat.Short(pose.Seconds)} poses";
        }
        var total = (double)(pose?.Seconds ?? 1);
        _progress.Width = _running.ActualWidth * (runner.Phase == RunnerPhase.Pose ? Math.Max(0, Math.Min(1, 1 - runner.Remaining / total)) : 0);

        var settings = _context.Settings.Current;
        var appeared = _notes.Update(_noteClock, runner.Index, runner.Poses.Count, _session.IsDrawing, FeatureFlags.Plan && settings.Notes);
        if (appeared && settings.Sound) SoundCue.Note();
        if (_notes.Shown != _shownNote)
        {
            _shownNote = _notes.Shown;
            _noteHolder.Content = _shownNote == null ? null : new SeaNoteCapsule(_shownNote, true);
        }
    }

    public string? ShownNote => _shownNote;

    void ShowImage(int index)
    {
        if (index == _shownIndex && _grayscale == _shownGray && _image.Source != null) return;
        if (!_rendered.TryGetValue(index, out var image))
        {
            _image.Source = null;
            return;
        }
        _shownIndex = index;
        _shownGray = _grayscale;
        _image.Source = _grayscale ? new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0) : image;
    }

    /// <summary>Renders the current and next pose off the UI thread and drops images outside the previous…next window.</summary>
    void Prefetch(int index)
    {
        var runner = _session.Runner;
        var (lower, upper) = RenderWindow.Indices(index, runner.Poses.Count);
        foreach (var key in _rendered.Keys.Where(k => k < lower || k > upper).ToList()) _rendered.Remove(key);
        foreach (var i in new[] { index, index + 1 })
        {
            if (i >= runner.Poses.Count || _rendered.ContainsKey(i) || !_session.ImagesById.TryGetValue(runner.Poses[i].ImageID, out var photo)) continue;
            var position = i;
            _ = Load(position, _library.RequestFor(photo, ScreenPixels));
        }
    }

    async Task Load(int position, RenderRequest request)
    {
        var image = await RenderCache.Shared.RenderAsync(request);
        // Ignore late results for a pose we've moved away from.
        var (lower, upper) = RenderWindow.Indices(_session.Runner.Index, _session.Runner.Poses.Count);
        if (image == null || position < lower || position > upper) return;
        _rendered[position] = image;
        _shownIndex = -1;
        Update();
    }

    void AskAttempt(PhotoRecord photo)
    {
        var number = _session.Runner.Current?.BenchmarkNumber ?? 1;
        Dispatcher.BeginInvoke(() =>
        {
            new AttemptSheet(_context, _library, photo, number, _session.SessionId) { Owner = this }.ShowDialog();
            _session.AttemptDone();
            Prefetch(_session.Runner.Index);
            _last = _clock.Elapsed;
            Update();
            Focus();
        });
    }

    void ShowSummary()
    {
        if (_finishedShown) return;
        _finishedShown = true;
        _timerTick.Stop();
        var runner = _session.Runner;
        var poses = runner.PoseElapsed.Count(e => e >= 5);
        var faded = new SolidColorBrush(Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF));
        var panel = Ui.Column(10,
            Ui.Text("That's your session.", 52, Ui.Medium, Brushes.White),
            Ui.Text($"{poses.ToString(CultureInfo.InvariantCulture)} poses · {DurationFormat.Total((int)runner.DrawingSeconds)}", 20, null, faded));
        if (_session.TickedKind is { } kind)
            Ui.Add(panel, Ui.Text($"{kind.Label()} ticked", 15, Ui.Medium, Brushes.White).Margin(0, 8), 10);
        else if (!_session.Recorded)
            Ui.Add(panel, Ui.Text(LineworkCopy.SessionTooShort, 15, null, faded).Margin(0, 8), 10);
        var close = Ui.Plain(new Border
        {
            MinHeight = 44,
            CornerRadius = new CornerRadius(22),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.White,
            Padding = new Thickness(18, 0, 18, 0),
            Background = Brushes.Transparent,
            Child = Ui.Text("CLOSE", 12, Ui.Medium, Brushes.White).Align(HorizontalAlignment.Center, VerticalAlignment.Center),
        }, Close, "Close").Align(HorizontalAlignment.Left).Margin(0, 20);
        Ui.Add(panel, close, 10);
        panel.Margin = new Thickness(72, 0, 72, 0);
        panel.VerticalAlignment = VerticalAlignment.Center;
        _page.Content = panel;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        var runner = _session.Runner;
        if (_session.AttemptFor != null) return;
        switch (e.Key)
        {
            case Key.Space:
                runner.TogglePause();
                break;
            case Key.Right:
                if (_session.Next().HasFlag(RunnerEvent.PoseChanged)) Prefetch(runner.Index);
                break;
            case Key.Left:
                _session.Previous();
                Prefetch(runner.Index);
                break;
            case Key.Escape:
                if (runner.Phase == RunnerPhase.Finished) Close();
                else _session.End();
                break;
            case Key.Enter when runner.Phase == RunnerPhase.Finished:
                Close();
                break;
            case Key.G:
                _grayscale = !_grayscale;
                break;
            case Key.T:
                _showTimer = !_showTimer;
                break;
            default:
                return;
        }
        e.Handled = true;
        Update();
    }

    void OnClosed(object? sender, EventArgs e)
    {
        _timerTick.Stop();
        SystemEvents.PowerModeChanged -= OnPower;
        _session.Closed();
        _anchor?.Activate();
    }
}

/// <summary>A note from s.e.a.: a thin capsule, signed.</summary>
public sealed class SeaNoteCapsule : Border
{
    public SeaNoteCapsule(string text, bool overBlack = false)
    {
        var ink = overBlack ? Brushes.White : Tokens.Ink;
        var muted = overBlack ? new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)) : Tokens.Muted;
        CornerRadius = new CornerRadius(20);
        Background = overBlack ? new SolidColorBrush(Color.FromArgb(0x77, 0x20, 0x20, 0x20)) : Tokens.Ground;
        BorderBrush = overBlack ? new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)) : Tokens.Hairline;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(18, 10, 18, 10);
        Child = Ui.Row(0, Ui.Text(text, 14, Ui.Medium, ink), Ui.Text("  — s.e.a.", 14, Ui.Medium, muted));
        System.Windows.Automation.AutomationProperties.SetName(this, $"{text} s.e.a.");
        IsHitTestVisible = false;
    }
}
