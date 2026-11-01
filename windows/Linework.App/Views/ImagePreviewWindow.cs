using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Linework.App.Library;
using Linework.App.Theme;

namespace Linework.App.Views;

/// <summary>Large preview of library images: ← → to step, O to compare with the uncropped original, E to edit the crop.</summary>
public sealed class ImagePreviewWindow : GlassWindow
{
    readonly LineworkContext _context;
    readonly LibraryStore _store;
    readonly IReadOnlyList<Guid> _ids;
    readonly Action<Guid> _onSelect;
    int _index;
    bool _showOriginal;

    public ImagePreviewWindow(LineworkContext context, LibraryStore store, IReadOnlyList<Guid> ids, int startIndex, Action<Guid> onSelect) : base(context.Settings)
    {
        _context = context;
        _store = store;
        _ids = ids;
        _onSelect = onSelect;
        _index = Math.Min(Math.Max(startIndex, 0), Math.Max(ids.Count - 1, 0));
        Title = "Preview";
        Width = 1000;
        Height = 800;
        MinWidth = 760;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        PreviewKeyDown += OnKey;
        Content = Body();
    }

    Linework.Core.Library.PhotoRecord? Current => _index < _ids.Count ? _store.Photo(_ids[_index]) : null;

    public FrameworkElement Body()
    {
        var dock = new DockPanel();
        var toolbar = Toolbar();
        DockPanel.SetDock(toolbar, Dock.Bottom);
        dock.Children.Add(toolbar);
        var stage = new Grid { Background = Brushes.Black };
        if (Current is { } current)
        {
            var thumb = new ThumbnailView { Margin = new Thickness(20) };
            thumb.Show(_store.RequestFor(current, 2000, showCrop: !_showOriginal));
            stage.Children.Add(thumb);
            // Pre-warm the neighbours so stepping feels instant.
            foreach (var i in new[] { _index - 1, _index + 1 })
                if (i >= 0 && i < _ids.Count && _store.Photo(_ids[i]) is { } neighbour)
                    _ = RenderCache.Shared.RenderAsync(_store.RequestFor(neighbour, 2000));
        }
        stage.Children.Add(Nav(Ui.ChevronLeftIcon, HorizontalAlignment.Left, _index > 0, () => Step(-1), "Previous"));
        stage.Children.Add(Nav(Ui.ChevronRightIcon, HorizontalAlignment.Right, _index < _ids.Count - 1, () => Step(1), "Next"));
        dock.Children.Add(stage);
        return dock;
    }

    static FrameworkElement Nav(string icon, HorizontalAlignment side, bool enabled, Action click, string label)
    {
        var border = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)),
            Child = Ui.Icon(icon, 20, Brushes.White, true),
        };
        var button = Ui.Plain(border, click, label).Align(side, VerticalAlignment.Center);
        button.Margin = new Thickness(12, 0, 12, 0);
        button.Visibility = enabled ? Visibility.Visible : Visibility.Hidden;
        return button;
    }

    FrameworkElement Toolbar()
    {
        var left = Ui.Row(12, Ui.Text($"{(_index + 1).ToString(CultureInfo.InvariantCulture)} of {_ids.Count.ToString(CultureInfo.InvariantCulture)}", 13, null, Tokens.Muted, tabular: true).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
        var right = Ui.Row(12);
        if (Current is { } current)
        {
            var benchmarks = Library.FeatureFlags.BenchmarksAndFreehand;
            if (benchmarks && _store.IsBenchmark(current.Id)) Ui.Add(left, Ui.Pill("Benchmark"), 12);
            var original = Ui.CapsuleToggle("Original", _showOriginal, _ => ToggleOriginal());
            original.ToolTip = "Show the whole photo (O)";
            Ui.Add(right, original, 12);
            if (benchmarks)
                Ui.Add(right, Ui.Capsule(_store.IsBenchmark(current.Id) ? "Unmark benchmark" : "Mark benchmark", () =>
                {
                    _store.ToggleBenchmark(current.Id);
                    Content = Body();
                }, 30), 12);
            var edit = Ui.Capsule("Edit crop…", Edit, 30);
            edit.ToolTip = "Edit the crop (E)";
            Ui.Add(right, edit, 12);
        }
        Ui.Add(right, Ui.Capsule("Done", Close, 30, true), 12);
        var dock = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };
        DockPanel.SetDock(left, Dock.Left);
        dock.Children.Add(left);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        dock.Children.Add(right);
        return new Border { Background = Tokens.Ground, Child = dock };
    }

    void ToggleOriginal()
    {
        _showOriginal = !_showOriginal;
        Content = Body();
    }

    void Edit()
    {
        if (Current is not { } current) return;
        CropEditorWindow.Edit(_context, _store, current, this);
        Content = Body();
    }

    void Step(int delta)
    {
        var next = Math.Min(Math.Max(_index + delta, 0), _ids.Count - 1);
        if (next == _index) return;
        _index = next;
        if (Current is { } current) _onSelect(current.Id);
        Content = Body();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Step(-1); break;
            case Key.Right: Step(1); break;
            case Key.Space or Key.Enter or Key.Escape: Close(); break;
            case Key.O: ToggleOriginal(); break;
            case Key.E: Edit(); break;
            default: return;
        }
        e.Handled = true;
    }
}
