using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Linework.App.Library;
using Linework.App.Platform;
using Linework.App.Theme;
using Linework.Core.Copy;
using Linework.Core.Crop;
using Linework.Core.Freehand;
using Microsoft.Win32;
using Size2 = Linework.Core.Freehand.Size2;

namespace Linework.App.Views;

/// <summary>The canvas, filling the area under the top bar. Back to front: clock, images, selection, tools, bar.
/// The images ignore clicks, so everything reaches this view, and the store does the hit-testing.</summary>
public sealed class FreehandView : UserControl
{
    const double WheelLine = 10, WheelNotch = 120, MarkSize = 10;

    readonly LineworkContext _context;
    readonly LibraryStore _library;
    readonly FreehandStore _store;
    readonly Grid _root = new() { Background = Brushes.Transparent, ClipToBounds = true, AllowDrop = true, Focusable = true, FocusVisualStyle = null };
    readonly StackPanel _clock = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    readonly TextBlock _emptyLine = Ui.Text(LineworkCopy.FreehandEmpty, 15, null, Tokens.Muted).Align(HorizontalAlignment.Center, VerticalAlignment.Top).Margin(0, 12);
    readonly Canvas _images = new() { IsHitTestVisible = false };
    readonly Canvas _marks = new() { IsHitTestVisible = false };
    readonly Canvas _ring = new();
    readonly Border _bar = new() { CornerRadius = new CornerRadius(23), Background = Tokens.Ground, BorderBrush = Tokens.Hairline, BorderThickness = new Thickness(1), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
    readonly Border _status = new() { CornerRadius = new CornerRadius(23), Background = Tokens.Ground, BorderBrush = Tokens.Hairline, BorderThickness = new Thickness(1), Padding = new Thickness(12, 0, 12, 0), Height = 46, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left };
    readonly ContentControl _note = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 78), IsHitTestVisible = false };
    readonly Dictionary<Guid, ItemVisual> _visuals = [];
    readonly FreehandNotes _notes;
    readonly DispatcherTimer _noteTimer = new() { Interval = TimeSpan.FromSeconds(0.5) };
    readonly Stopwatch _clockWatch = Stopwatch.StartNew();
    double _noteClock;
    TimeSpan _noteLast;
    string? _shownNote;
    string _barKey = "";
    bool _pending, _cropOpen;

    public FreehandView(LineworkContext context, LibraryStore library, FreehandStore store)
    {
        _context = context;
        _library = library;
        _store = store;
        _notes = new FreehandNotes(context.Settings.Current.Name, Random.Shared);
        _clock.Children.Add(new HeroClock(context));
        _clock.Children.Add(_emptyLine);
        _root.Children.Add(_clock);
        _root.Children.Add(_images);
        _root.Children.Add(_marks);
        _root.Children.Add(_ring);
        _bar.Margin = new Thickness(0, 0, 0, 20);
        _root.Children.Add(_note);
        _root.Children.Add(_bar);
        _root.Children.Add(_status);
        Content = _root;

        _root.SizeChanged += (_, e) =>
        {
            store.Viewport = new Size2(e.NewSize.Width, e.NewSize.Height);
            Schedule();
        };
        _bar.SizeChanged += (_, e) =>
        {
            store.BarWidth = e.NewSize.Width;
            Schedule();
        };
        // Clicks on the bar and the ring are their own.
        _root.MouseLeftButtonDown += (_, e) =>
        {
            _root.Focus();
            _root.CaptureMouse();
            store.MouseDown(At(e), e.ClickCount);
            e.Handled = true;
        };
        _root.MouseMove += (_, e) =>
        {
            if (_root.IsMouseCaptured) store.MouseDragged(At(e), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        };
        _root.MouseLeftButtonUp += (_, _) =>
        {
            _root.ReleaseMouseCapture();
            store.MouseUp();
        };
        _root.LostMouseCapture += (_, _) => store.CancelDrag();
        _root.MouseWheel += (_, e) =>
        {
            var notches = e.Delta / WheelNotch;
            // Ctrl with the wheel is also how a touchpad's pinch arrives.
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) store.Magnify(Math.Pow(1.1, notches), At(e));
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) store.Scroll(new Size2(notches * 3 * WheelLine, 0));
            else store.Scroll(new Size2(0, notches * 3 * WheelLine));
            e.Handled = true;
        };
        _root.KeyDown += OnKey;
        _root.Drop += async (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) await store.ImportFilesAsync(files, At(e.GetPosition(_root)));
        };
        void OnChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e) => Schedule();
        void OnPower(object? s, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) Dispatcher.BeginInvoke(() => _notes.Sleep());
        }
        Loaded += (_, _) =>
        {
            store.LoadIfNeeded();
            store.PropertyChanged += OnChanged;
            SystemEvents.PowerModeChanged += OnPower;
            _noteLast = _clockWatch.Elapsed;
            _noteTimer.Start();
            _root.Focus();
            Sync();
        };
        Unloaded += (_, _) =>
        {
            store.PropertyChanged -= OnChanged;
            SystemEvents.PowerModeChanged -= OnPower;
            _noteTimer.Stop();
            store.Leave();
        };
        _noteTimer.Tick += (_, _) => TickNotes();
        store.LoadIfNeeded();
        Sync();
    }

    static Point2 At(Point p) => new(p.X, p.Y);
    Point2 At(MouseEventArgs e) => At(e.GetPosition(_root));

    void Schedule()
    {
        if (_pending) return;
        _pending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _pending = false;
            Sync();
        });
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.Delete or Key.Back when Keyboard.Modifiers == ModifierKeys.None: _store.DeleteSelected(); break;
            case Key.Escape: _store.Escape(); break;
            case Key.OemPlus or Key.Add when ctrl: _store.ZoomStep(true); break;
            case Key.OemMinus or Key.Subtract when ctrl: _store.ZoomStep(false); break;
            default: return;
        }
        e.Handled = true;
    }

    void TickNotes()
    {
        var now = _clockWatch.Elapsed;
        var dt = (now - _noteLast).TotalSeconds;
        _noteLast = now;
        // A long gap is the machine asleep, which never counts.
        if (dt < 5) _noteClock += dt;
        var settings = _context.Settings.Current;
        var usual = SeaNotes.UsualSeconds(_library.Sessions.Select(s => s.DrawingSeconds).ToList());
        var appeared = _notes.Update(_noteClock, _store.IsInteracting || _store.RingAt != null, settings.Notes, usual);
        if (appeared && settings.Sound) SoundCue.Note();
        if (_notes.Shown == _shownNote) return;
        _shownNote = _notes.Shown;
        _note.Content = _shownNote == null ? null : new SeaNoteCapsule(_shownNote);
    }

    /// <summary>Makes the screen show the store. Called at most once a frame.</summary>
    public void Sync()
    {
        var doc = _store.Document;
        _emptyLine.Visibility = doc.Items.Count == 0 ? Visibility.Visible : Visibility.Hidden;
        SyncImages(doc);
        SyncMarks(doc);
        SyncRing();
        SyncBar(doc);
        OpenCropIfAsked(doc);
    }

    void SyncImages(FreehandDocument doc)
    {
        var ids = doc.Items.Select(i => i.Id).ToHashSet();
        foreach (var gone in _visuals.Keys.Where(k => !ids.Contains(k)).ToList())
        {
            _images.Children.Remove(_visuals[gone].Element);
            _visuals.Remove(gone);
        }
        for (var z = 0; z < doc.Items.Count; z++)
        {
            var item = doc.Items[z];
            if (!_visuals.TryGetValue(item.Id, out var visual))
            {
                visual = new ItemVisual();
                _visuals[item.Id] = visual;
                _images.Children.Add(visual.Element);
            }
            Panel.SetZIndex(visual.Element, z);
            visual.Show(item, _store.Files.ImagePath(item.FileName), doc.Camera, _store.Viewport, doc.Greyscale);
        }
    }

    void SyncMarks(FreehandDocument doc)
    {
        _marks.Children.Clear();
        if (doc.Items.FirstOrDefault(i => i.Id == _store.SelectedId) is not { } selected) return;
        var camera = doc.Camera;
        double w = selected.VisibleSize.Width * camera.Zoom, h = selected.VisibleSize.Height * camera.Zoom;
        var center = camera.ToScreen(selected.Center);
        var outline = new Rectangle { Width = w, Height = h, Stroke = Tokens.Ink, StrokeThickness = 1, RenderTransform = new RotateTransform(selected.Rotation, w / 2, h / 2) };
        Canvas.SetLeft(outline, center.X - w / 2);
        Canvas.SetTop(outline, center.Y - h / 2);
        _marks.Children.Add(outline);
        var corners = FreehandGeometry.Corners(selected);
        if (_store.Tool == FreehandTool.Resize)
        {
            foreach (var corner in corners)
            {
                var p = camera.ToScreen(corner);
                var mark = new Rectangle { Width = MarkSize, Height = MarkSize, Fill = Tokens.Ground, Stroke = Tokens.Ink, StrokeThickness = 1 };
                Canvas.SetLeft(mark, p.X - MarkSize / 2);
                Canvas.SetTop(mark, p.Y - MarkSize / 2);
                _marks.Children.Add(mark);
            }
        }
        else if (_store.Tool == FreehandTool.Rotate)
        {
            var top = camera.ToScreen(new Point2((corners[0].X + corners[1].X) / 2, (corners[0].Y + corners[1].Y) / 2));
            var handle = camera.ToScreen(FreehandGeometry.RotateHandle(selected, camera.Zoom));
            _marks.Children.Add(new Line { X1 = top.X, Y1 = top.Y, X2 = handle.X, Y2 = handle.Y, Stroke = Tokens.Ink, StrokeThickness = 1 });
            var size = MarkSize + 4;
            var dot = new Ellipse { Width = size, Height = size, Fill = Tokens.Ground, Stroke = Tokens.Ink, StrokeThickness = 1 };
            Canvas.SetLeft(dot, handle.X - size / 2);
            Canvas.SetTop(dot, handle.Y - size / 2);
            _marks.Children.Add(dot);
        }
    }

    /// <summary>The circular menu opened by double-clicking an image: five round buttons, drawn above the images at constant screen size.</summary>
    void SyncRing()
    {
        _ring.Children.Clear();
        if (_store.RingAt is not { } at) return;
        var center = FreehandRing.Center(at, _store.Viewport, _store.BarWidth);
        var offsets = FreehandRing.Offsets();
        (string Icon, string Name, Action Act)[] entries =
        [
            (Ui.ResizeIcon, "Resize", () => _store.ChooseTool(FreehandTool.Resize)),
            (Ui.RotateIcon, "Rotate", () => _store.ChooseTool(FreehandTool.Rotate)),
            (Ui.FlipIcon, "Flip", _store.FlipSelected),
            (Ui.CropIcon, "Crop", _store.BeginCrop),
            (Ui.TrashIcon, "Remove", _store.DeleteSelected),
        ];
        for (var i = 0; i < entries.Length; i++)
        {
            var (icon, name, act) = entries[i];
            var disc = new Grid { Width = FreehandRing.DiscSize, Height = FreehandRing.DiscSize };
            disc.Children.Add(new Ellipse { Fill = Tokens.Ground, Opacity = 0.85 });
            disc.Children.Add(Ui.Circle(Ui.Icon(icon, 16, stroke: true), act, name, FreehandRing.ButtonSize).Align(HorizontalAlignment.Center, VerticalAlignment.Center));
            disc.MouseLeftButtonDown += (_, e) => e.Handled = true;
            Canvas.SetLeft(disc, center.X + offsets[i].X - FreehandRing.DiscSize / 2);
            Canvas.SetTop(disc, center.Y + offsets[i].Y - FreehandRing.DiscSize / 2);
            _ring.Children.Add(disc);
        }
    }

    /// <summary>The floating bar: the canvas menu, Greyscale, the zoom percentage (fits all images) and Clear.
    /// A status message hangs off the right edge as its own capsule, so showing it never moves the controls.</summary>
    void SyncBar(FreehandDocument doc)
    {
        var zoom = ((int)Math.Round(doc.Camera.Zoom * 100, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + "%";
        var key = string.Join('|', _store.CurrentName, doc.Greyscale, zoom, doc.Items.Count == 0);
        if (key != _barKey)
        {
            _barKey = key;
            var menu = Ui.Capsule(_store.CurrentName, () => { }, 30);
            menu.Click += (_, _) => CanvasMenu.Show(menu, _store);
            System.Windows.Automation.AutomationProperties.SetName(menu, $"Canvas, {_store.CurrentName}");
            var fit = Ui.Capsule(zoom, _store.Fit, 30);
            fit.MinWidth = 72;
            fit.ToolTip = "Fit all images in the window";
            System.Windows.Automation.AutomationProperties.SetName(fit, "Zoom, fit all images");
            var clear = Ui.Capsule("Clear", () =>
            {
                if (AlertWindow.Ask("Clear your canvas?", "This removes all images from Freehand. Your original files are safe.", "Clear", "Cancel") == 0) _store.Clear();
            }, 30);
            clear.IsEnabled = doc.Items.Count > 0;
            _bar.Child = Ui.Row(10, menu, Ui.CapsuleToggle("Greyscale", doc.Greyscale, _store.SetGreyscale), fit, clear);
            _bar.MouseLeftButtonDown += (_, e) => e.Handled = true;
        }
        if (_store.Status is { } status)
        {
            _status.Visibility = Visibility.Visible;
            _status.Child = Ui.Text(status, 12, Ui.Medium, Tokens.Muted).Align(HorizontalAlignment.Left, VerticalAlignment.Center);
            _status.Margin = new Thickness((_store.Viewport.Width + _store.BarWidth) / 2 + 8, 0, 0, 20);
        }
        else
        {
            _status.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Freehand's editor: the image looks as it does on the canvas, and the canvas keeps its own Rotate and Flip.</summary>
    void OpenCropIfAsked(FreehandDocument doc)
    {
        if (_cropOpen || _store.CropId is not { } id || doc.Items.FirstOrDefault(i => i.Id == id) is not { } item) return;
        if (AlertWindow.Silent) return;
        _cropOpen = true;
        Dispatcher.BeginInvoke(() =>
        {
            new CropEditorWindow(_context, _store.Files.ImagePath(item.FileName), new CropEdit(item.ShownCrop, 0, item.Flipped), false,
                edit => _store.ApplyCrop(item.StoredCrop(edit.Crop))) { Owner = Window.GetWindow(this) }.ShowDialog();
            _cropOpen = false;
            _store.EndCrop();
            _root.Focus();
        });
    }

    /// <summary>One image on the canvas, positioned in screen space. A hairline frame shows until it's decoded.</summary>
    sealed class ItemVisual
    {
        readonly Image _image = new() { Stretch = Stretch.Fill };
        readonly Border _frame = new() { BorderBrush = Tokens.Hairline };
        RenderRequest? _request;
        NormalizedRect? _decodedCrop;
        BitmapSource? _decoded;
        bool _grey;

        public FrameworkElement Element => _frame;

        public ItemVisual()
        {
            _frame.Child = _image;
            _frame.RenderTransformOrigin = new Point(0.5, 0.5);
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        }

        public void Show(FreehandItem item, string path, FreehandCamera camera, Size2 viewport, bool greyscale)
        {
            double w = item.VisibleSize.Width * camera.Zoom, h = item.VisibleSize.Height * camera.Zoom;
            var center = camera.ToScreen(item.Center);
            _frame.Width = Math.Max(w, 0);
            _frame.Height = Math.Max(h, 0);
            Canvas.SetLeft(_frame, center.X - w / 2);
            Canvas.SetTop(_frame, center.Y - h / 2);
            _frame.RenderTransform = new TransformGroup { Children = { new ScaleTransform(item.Flipped ? -1 : 1, 1), new RotateTransform(item.Rotation) } };
            var request = new RenderRequest(path, 0, false, item.Crop, FreehandGeometry.DecodePixels(item, camera, viewport));
            _grey = greyscale;
            if (request != _request)
            {
                _request = request;
                if (RenderCache.Shared.Get(request) is { } hit) Decoded(hit, item.Crop);
                else _ = Load(request);
            }
            // Another crop's pixels must never fill this frame.
            var ready = _decoded != null && _decodedCrop == item.Crop;
            // Over the ground, so a transparent PNG doesn't let the clock through.
            _frame.Background = ready ? Tokens.Ground : null;
            _frame.BorderThickness = new Thickness(ready ? 0 : 1);
            _image.Visibility = ready ? Visibility.Visible : Visibility.Hidden;
            ShowImage();
        }

        void ShowImage()
        {
            if (_decoded == null) return;
            BitmapSource shown = _grey ? new FormatConvertedBitmap(_decoded, PixelFormats.Gray8, null, 0) : _decoded;
            if (_image.Source is FormatConvertedBitmap == _grey && _image.Source != null && _shownFrom == _decoded) return;
            _image.Source = shown;
            _shownFrom = _decoded;
        }

        BitmapSource? _shownFrom;

        void Decoded(BitmapSource image, NormalizedRect crop)
        {
            _decoded = image;
            _decodedCrop = crop;
        }

        async Task Load(RenderRequest request)
        {
            var image = await RenderCache.Shared.RenderAsync(request);
            if (image == null || request != _request) return;
            Decoded(image, request.Crop);
            _frame.Background = Tokens.Ground;
            _frame.BorderThickness = default;
            _image.Visibility = Visibility.Visible;
            ShowImage();
        }
    }
}

/// <summary>Freehand's canvas list: the current canvas, the others, and what can be done with them. Everything autosaves.</summary>
public static class CanvasMenu
{
    public static void Show(FrameworkElement target, FreehandStore store)
    {
        store.RefreshList();
        var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        MenuItem Item(string title, Action click, bool isChecked = false)
        {
            var item = new MenuItem { Header = title, IsChecked = isChecked };
            item.Click += (_, _) => click();
            return item;
        }
        foreach (var name in store.Names) menu.Items.Add(Item(name, () => store.Open(name), name == store.CurrentName));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("New Canvas", () => Attempt(store.NewCanvas)));
        menu.Items.Add(Item("Rename…", () =>
        {
            if (AlertWindow.AskText("Rename canvas", store.CurrentName, "Rename") is { } name) Attempt(() => store.RenameCurrent(name));
        }));
        menu.Items.Add(Item("Duplicate", () => Attempt(store.DuplicateCurrent)));
        menu.Items.Add(Item("Delete…", () =>
        {
            if (AlertWindow.Ask($"Delete “{store.CurrentName}”?", "The canvas and its image copies move to the Recycle Bin.", "Delete", "Cancel") == 0) Attempt(store.DeleteCurrent);
        }));
        menu.IsOpen = true;
    }

    static void Attempt(Action action)
    {
        string problem;
        try
        {
            action();
            return;
        }
        catch (CanvasNameException e) when (e.Error == CanvasNameError.Invalid)
        {
            problem = "That name can't be used. Names can't be empty, start or end with a dot, contain < > : \" / \\ | ? *, or be a name Windows reserves, like CON.";
        }
        catch (CanvasNameException)
        {
            problem = "You already have a canvas with that name.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException or Linework.Core.Library.LibraryUnavailableException)
        {
            problem = "That didn't work. Check that the library's folder is available.";
        }
        AlertWindow.Ask(problem, "", "OK");
    }
}
