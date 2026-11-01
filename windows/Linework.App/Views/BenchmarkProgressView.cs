using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Linework.App.Library;
using Linework.App.Theme;
using Linework.Core.Copy;
using Linework.Core.Crop;
using Linework.Core.Library;
using Linework.Core.Plan;
using Linework.Core.Session;
using Microsoft.Win32;

namespace Linework.App.Views;

public sealed class BenchmarkProgressView : StoreView
{
    static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    readonly LineworkContext _context;
    readonly LibraryStore _library;
    readonly ScrollViewer _scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };

    public BenchmarkProgressView(LineworkContext context, LibraryStore library) : base(library)
    {
        _context = context;
        _library = library;
        Refresh();
    }

    protected override UIElement Render()
    {
        var benchmarks = _library.VisiblePhotos.Where(p => _library.IsBenchmark(p.Id)).OrderBy(p => p.ImportedAt).ToList();
        if (benchmarks.Count == 0)
        {
            _scroller.Content = Ui.Text(LineworkCopy.EmptyBenchmarks, 26, Ui.Medium).Margin(72, 96, 72);
            return _scroller;
        }
        var list = Ui.Column(44);
        var dates = _library.Sessions.Select(s => s.StartedAt).ToList();
        foreach (var photo in benchmarks) Ui.Add(list, Row(photo, dates), 44);
        list.Margin = new Thickness(72, 40, 72, 40);
        _scroller.Content = list;
        return _scroller;
    }

    FrameworkElement Row(PhotoRecord photo, IReadOnlyList<DateTimeOffset> dates)
    {
        var record = _library.Benchmark(photo.Id)!;
        var left = Math.Max(record.Interval, 1) - BenchmarkSchedule.SessionsSince(_library.BenchmarkSince(record), dates);
        var dueText = left <= 0 ? "Due next session" : $"Next in {left} session{(left == 1 ? "" : "s")}";
        var controls = Ui.Row(24,
            Ui.Stepper($"Duration {DurationFormat.Short(record.Seconds)}",
                () => _library.UpdateBenchmark(photo.Id, seconds: Math.Max(record.Seconds - 15, 15)),
                () => _library.UpdateBenchmark(photo.Id, seconds: Math.Min(record.Seconds + 15, 1800))),
            Ui.Stepper($"Every {record.Interval} sessions",
                () => _library.UpdateBenchmark(photo.Id, interval: Math.Max(record.Interval - 1, 1)),
                () => _library.UpdateBenchmark(photo.Id, interval: Math.Min(record.Interval + 1, 100))));
        var head = new DockPanel();
        DockPanel.SetDock(controls, Dock.Right);
        head.Children.Add(controls);
        head.Children.Add(Ui.Text(dueText, 20, Ui.Medium));

        var tiles = Ui.Row(16);
        var reference = new ThumbnailView { Width = 150, Height = 190 };
        reference.Show(_library.RequestFor(photo, 400));
        Ui.Add(tiles, Ui.Column(6, reference, Ui.Text("Reference", 12, Ui.SemiBold).Align(HorizontalAlignment.Center, VerticalAlignment.Top)), 16);
        Ui.Add(tiles, new Border { Width = 1, Height = 190, Background = Tokens.Hairline, VerticalAlignment = VerticalAlignment.Top }, 16);
        var attempts = _library.Attempts(photo.Id);
        if (attempts.Count == 0) Ui.Add(tiles, new Border { Height = 190, Child = Ui.Text("No attempts yet", 13, null, Tokens.Muted).Align(HorizontalAlignment.Left, VerticalAlignment.Center) }, 16);
        foreach (var attempt in attempts)
            Ui.Add(tiles, Ui.Column(6, AttemptTile(attempt),
                Ui.Text(attempt.Date.ToLocalTime().ToString("MMM d, yyyy", English), 12, null, Tokens.Muted).Align(HorizontalAlignment.Center, VerticalAlignment.Top)), 16);
        tiles.Margin = new Thickness(0, 0, 0, 8);
        return Ui.Column(16, head, new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = tiles, Focusable = false });
    }

    FrameworkElement AttemptTile(AttemptRecord attempt)
    {
        if (attempt.DrawingFileName is { } name)
        {
            var drawing = new ThumbnailView { Width = 150, Height = 190 };
            drawing.Show(new RenderRequest(_library.DrawingPath(name), 0, false, CropMath.Unit, 600));
            return Ui.Plain(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = drawing }, () =>
                new ComparisonWindow(_context, _library, attempt) { Owner = Window.GetWindow(this) }.ShowDialog(), "Compare with reference");
        }
        return Ui.Plain(new Border
        {
            Width = 150,
            Height = 190,
            CornerRadius = new CornerRadius(8),
            BorderBrush = Tokens.Hairline,
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
            Child = Ui.Column(6, Ui.Icon(Ui.PlusIcon, 14, stroke: true).Align(HorizontalAlignment.Center, VerticalAlignment.Top), Ui.Text("Add drawing", 13)).Align(HorizontalAlignment.Center, VerticalAlignment.Center),
        }, () => Attach(attempt));
    }

    void Attach(AttemptRecord attempt)
    {
        var dialog = new OpenFileDialog { Title = "Add drawing", Filter = "Images|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.webp;*.tif;*.tiff;*.gif;*.bmp|All files|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            _library.AttachDrawing(attempt.Id, dialog.FileName);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or LibraryUnavailableException)
        {
            AlertWindow.Ask("That file couldn’t be read as an image.", "", "OK");
        }
    }
}

public sealed class ComparisonWindow : GlassWindow
{
    public ComparisonWindow(LineworkContext context, LibraryStore library, AttemptRecord attempt) : base(context.Settings)
    {
        Title = "Compare";
        Width = 1000;
        Height = 720;
        MinWidth = 900;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        KeyDown += (_, e) =>
        {
            if (e.Key is System.Windows.Input.Key.Escape or System.Windows.Input.Key.Enter) Close();
        };
        Content = Body(library, attempt, Close);
    }

    public static FrameworkElement Body(LibraryStore library, AttemptRecord attempt, Action done)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        static FrameworkElement Side(ThumbnailView image, string caption)
        {
            var dock = new DockPanel();
            var label = Ui.Text(caption, 14, Ui.SemiBold).Align(HorizontalAlignment.Center, VerticalAlignment.Top).Margin(0, 10);
            DockPanel.SetDock(label, Dock.Bottom);
            dock.Children.Add(label);
            dock.Children.Add(image);
            return dock;
        }
        if (library.Photo(attempt.PhotoID) is { } reference)
        {
            var image = new ThumbnailView();
            image.Show(library.RequestFor(reference, 1600));
            var side = Side(image, "Reference").Margin(0, 0, 12);
            grid.Children.Add(side);
        }
        if (attempt.DrawingFileName is { } name)
        {
            var image = new ThumbnailView();
            image.Show(new RenderRequest(library.DrawingPath(name), 0, false, CropMath.Unit, 1600));
            var side = Side(image, attempt.Date.ToLocalTime().ToString("MMMM d, yyyy", CultureInfo.GetCultureInfo("en-US"))).Margin(12);
            Grid.SetColumn(side, 1);
            grid.Children.Add(side);
        }
        var dock = new DockPanel { Margin = new Thickness(28) };
        var end = Ui.Capsule("Done", done, 30, true).Align(HorizontalAlignment.Right).Margin(0, 20);
        DockPanel.SetDock(end, Dock.Bottom);
        dock.Children.Add(end);
        dock.Children.Add(grid);
        return dock;
    }
}
