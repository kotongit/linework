using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Linework.App.Library;
using Linework.App.Theme;
using Linework.Core.Crop;
using Linework.Core.Library;
using Path = System.Windows.Shapes.Path;

namespace Linework.App.Views;

/// <summary>A crop, rotation and flip, as edited together.</summary>
public sealed record CropEdit(NormalizedRect Crop, int Rotation, bool Flipped);

/// <summary>Edits a draft of an image's crop, rotation and flip; nothing is reported until Save.</summary>
public sealed class CropEditorWindow : GlassWindow
{
    static readonly (string Title, double? Value)[] Aspects = [("Free", null), ("2:3", 2.0 / 3.0), ("3:4", 3.0 / 4.0), ("1:1", 1)];

    readonly bool _showsOrientation;
    readonly Action<CropEdit> _onSave;
    readonly Canvas _canvas = new() { Background = Brushes.Black, ClipToBounds = true };
    readonly StackPanel _controls = new() { Orientation = Orientation.Horizontal };
    readonly StackPanel _end = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    readonly string _path;
    BitmapSource? _base, _oriented;
    NormalizedRect _crop;
    int _rotation;
    bool _flipped;
    double? _aspect;
    bool _detecting;

    public CropEditorWindow(LineworkContext context, string path, CropEdit initial, bool showsOrientation, Action<CropEdit> onSave) : base(context.Settings)
    {
        _path = path;
        _crop = initial.Crop;
        _rotation = initial.Rotation;
        _flipped = initial.Flipped;
        _showsOrientation = showsOrientation;
        _onSave = onSave;
        Title = "Edit Crop";
        Width = 900;
        Height = 700;
        MinWidth = 880;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var dock = new DockPanel();
        var bar = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };
        DockPanel.SetDock(_controls, Dock.Left);
        bar.Children.Add(_controls);
        bar.Children.Add(_end);
        var barHost = new Border { Background = Tokens.Ground, Child = bar };
        DockPanel.SetDock(barHost, Dock.Bottom);
        dock.Children.Add(barHost);
        dock.Children.Add(new Border { Background = Brushes.Black, Padding = new Thickness(20), Child = _canvas });
        Content = dock;
        _canvas.SizeChanged += (_, _) => DrawCanvas();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) Save();
        };
        DrawControls();
        Loaded += async (_, _) =>
        {
            _base = await Task.Run(() => ImageRendering.Load(path, 1600));
            Reorient();
        };
    }

    /// <summary>The Library's editor: writes the result back to the photo, clearing "no figure found" once cropped.</summary>
    public static void Edit(LineworkContext context, LibraryStore store, PhotoRecord photo, Window? owner)
    {
        new CropEditorWindow(context, store.PhotoPath(photo), new CropEdit(photo.Crop, photo.Rotation, photo.Flipped), true, edit =>
            store.Update(photo.Id, r => r with
            {
                Crop = edit.Crop,
                Rotation = edit.Rotation,
                Flipped = edit.Flipped,
                NoFigureFound = r.NoFigureFound && edit.Crop == CropMath.Unit,
            })) { Owner = owner }.ShowDialog();
    }

    double ImageAspect => _oriented == null ? 1 : (double)_oriented.PixelWidth / _oriented.PixelHeight;

    void Reorient()
    {
        if (_base == null) return;
        _oriented = ImageRendering.Oriented(_base, _rotation, _flipped);
        DrawControls();
        DrawCanvas();
    }

    (double X, double Y, double W, double H) Frame()
    {
        double sw = _canvas.ActualWidth, sh = _canvas.ActualHeight;
        if (_oriented == null || sw <= 0 || sh <= 0) return (0, 0, 1, 1);
        var scale = Math.Min(sw / _oriented.PixelWidth, sh / _oriented.PixelHeight);
        double w = _oriented.PixelWidth * scale, h = _oriented.PixelHeight * scale;
        return ((sw - w) / 2, (sh - h) / 2, w, h);
    }

    void DrawCanvas()
    {
        _canvas.Children.Clear();
        if (_oriented == null) return;
        var (fx, fy, fw, fh) = Frame();
        var image = new Image { Source = _oriented, Width = fw, Height = fh, Stretch = Stretch.Fill };
        Canvas.SetLeft(image, fx);
        Canvas.SetTop(image, fy);
        _canvas.Children.Add(image);
        double bx = fx + _crop.X * fw, by = fy + _crop.Y * fh, bw = _crop.Width * fw, bh = _crop.Height * fh;
        _canvas.Children.Add(new Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)),
            IsHitTestVisible = false,
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(fx, fy, fw, fh)), new RectangleGeometry(new Rect(bx, by, bw, bh))),
        });
        var box = new Rectangle { Width = bw, Height = bh, Stroke = Brushes.White, StrokeThickness = 1, Fill = Brushes.Transparent, Cursor = Cursors.SizeAll };
        Canvas.SetLeft(box, bx);
        Canvas.SetTop(box, by);
        Drag(box, (start, dx, dy) => CropMath.Moved(start, dx, dy));
        _canvas.Children.Add(box);
        foreach (var handle in Enum.GetValues<CropHandle>())
        {
            var (hx, hy) = handle.Direction();
            var x = hx < 0 ? bx : hx > 0 ? bx + bw : bx + bw / 2;
            var y = hy < 0 ? by : hy > 0 ? by + bh : by + bh / 2;
            var grip = new Border
            {
                Width = 24,
                Height = 24,
                Background = Brushes.Transparent,
                Cursor = (hx * hy) switch { 1 => Cursors.SizeNWSE, -1 => Cursors.SizeNESW, _ => hx != 0 ? Cursors.SizeWE : Cursors.SizeNS },
                Child = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(2), Background = Brushes.White, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1) },
            };
            Canvas.SetLeft(grip, x - 12);
            Canvas.SetTop(grip, y - 12);
            Drag(grip, (start, dx, dy) => CropMath.Resized(start, handle, dx, dy, _aspect, ImageAspect));
            _canvas.Children.Add(grip);
        }
    }

    /// <summary>The canvas is redrawn as the box moves, so the drag lives on the canvas rather than on what started it.</summary>
    void Drag(FrameworkElement element, Func<NormalizedRect, double, double, NormalizedRect> apply)
    {
        element.MouseLeftButtonDown += (_, e) =>
        {
            var origin = e.GetPosition(_canvas);
            var start = _crop;
            var (_, _, fw, fh) = Frame();
            MouseEventHandler? move = null;
            MouseButtonEventHandler? up = null;
            move = (_, m) =>
            {
                var p = m.GetPosition(_canvas);
                _crop = apply(start, (p.X - origin.X) / fw, (p.Y - origin.Y) / fh);
                DrawCanvas();
            };
            up = (_, _) =>
            {
                _canvas.MouseMove -= move;
                _canvas.MouseLeftButtonUp -= up;
                _canvas.ReleaseMouseCapture();
            };
            _canvas.MouseMove += move;
            _canvas.MouseLeftButtonUp += up;
            _canvas.CaptureMouse();
            e.Handled = true;
        };
    }

    void DrawControls()
    {
        _controls.Children.Clear();
        var ready = _oriented != null && !_detecting;
        foreach (var (title, value) in Aspects)
            Ui.Add(_controls, Ui.Capsule(title, () =>
            {
                _aspect = value;
                if (value is { } v) _crop = CropMath.FitAspect(_crop, v, ImageAspect);
                DrawControls();
                DrawCanvas();
            }, 30, _aspect == value), _controls.Children.Count == 0 ? 0 : 4);
        var auto = Ui.Capsule("Auto", RunAuto, 30);
        auto.IsEnabled = ready;
        Ui.Add(_controls, auto, 10);
        Ui.Add(_controls, Ui.Capsule("Reset", () =>
        {
            _crop = CropMath.Unit;
            DrawCanvas();
        }, 30), 10);
        if (_showsOrientation)
        {
            var rotate = Ui.Capsule("Rotate", () =>
            {
                // Rotation is applied before the mirror, so a flipped image turns the other way.
                _rotation = (_rotation + (_flipped ? 270 : 90)) % 360;
                _crop = CropMath.Unit; // the old normalized crop no longer matches the rotated frame
                Reorient();
            }, 30);
            rotate.ToolTip = "Rotate 90°";
            Ui.Add(_controls, rotate, 10);
            Ui.Add(_controls, Ui.Capsule("Flip", () =>
            {
                _flipped = !_flipped;
                _crop = CropMath.MirroredX(_crop);
                Reorient();
            }, 30), 10);
        }
        _end.Children.Clear();
        Ui.Add(_end, Ui.Capsule("Cancel", Close, 30), 10);
        Ui.Add(_end, Ui.Capsule("Save", Save, 30, true), 10);
    }

    async void RunAuto()
    {
        if (_oriented is not { } oriented) return;
        if (!oriented.IsFrozen) oriented.Freeze();
        _detecting = true;
        DrawControls();
        var (rect, _) = await Task.Run(() => AutoCropper.Crop(oriented));
        _crop = rect;
        if (_aspect is { } v) _crop = CropMath.FitAspect(_crop, v, ImageAspect);
        _detecting = false;
        DrawControls();
        DrawCanvas();
    }

    void Save()
    {
        _onSave(new CropEdit(_crop, _rotation, _flipped));
        Close();
    }

    public static CropEditorWindow ForShot(LineworkContext context, string path) => new(context, path, new CropEdit(new NormalizedRect(0.1, 0.1, 0.6, 0.8), 0, false), true, _ => { });
}
