using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Linework.App.Library;
using Linework.App.Theme;
using Linework.Core.Copy;
using Linework.Core.Library;
using Microsoft.Win32;

namespace Linework.App.Views;

public abstract record LibraryFilter;
public sealed record FilterAll : LibraryFilter;
public sealed record FilterBenchmarks : LibraryFilter;
public sealed record FilterSet(Guid Id) : LibraryFilter;

/// <summary>A virtualized row of tiles. The owner re-chunks these when the width changes.</summary>
public sealed record PhotoRow(LibraryView Owner, IReadOnlyList<PhotoRecord> Photos, string Signature);

public sealed class LibraryView : UserControl
{
    const double TileMin = 160, Spacing = 16, SidePadding = 72;

    readonly LineworkContext _context;
    readonly LibraryStore _library;
    readonly StackPanel _filterRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(SidePadding, 24, SidePadding, 16) };
    readonly ItemsControl _grid = new();
    readonly Border _empty = new();
    readonly Border _status = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16) };
    readonly Border _dropRing = new() { CornerRadius = new CornerRadius(16), BorderBrush = Tokens.Ink, BorderThickness = new Thickness(1), Margin = new Thickness(16), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    LibraryFilter _filter = new FilterAll();
    Guid? _selection;
    int _columns = 1;
    string _filterSignature = "", _gridSignature = "";
    bool _importing;
    string? _status_;
    bool _renderPending;

    public LibraryView(LineworkContext context, LibraryStore library)
    {
        _context = context;
        _library = library;
        Focusable = true;
        FocusVisualStyle = null;

        var tile = new FrameworkElementFactory(typeof(PhotoRowView));
        _grid.ItemTemplate = new DataTemplate { VisualTree = tile };
        var scroller = new FrameworkElementFactory(typeof(ScrollViewer));
        scroller.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroller.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scroller.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroller.SetValue(FocusableProperty, false);
        scroller.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        _grid.Template = new ControlTemplate(typeof(ItemsControl)) { VisualTree = scroller };
        var panel = new FrameworkElementFactory(typeof(VirtualizingStackPanel));
        _grid.ItemsPanel = new ItemsPanelTemplate(panel);
        VirtualizingPanel.SetIsVirtualizing(_grid, true);
        VirtualizingPanel.SetVirtualizationMode(_grid, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(_grid, ScrollUnit.Pixel);
        _grid.Margin = new Thickness(SidePadding, 8, SidePadding, 0);
        _grid.Focusable = false;
        _grid.SizeChanged += (_, _) => Draw();

        var layout = new Grid { AllowDrop = true, Background = Brushes.Transparent };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        var strip = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _filterRow, Focusable = false };
        layout.Children.Add(strip);
        Grid.SetRow(_grid, 1);
        Grid.SetRow(_empty, 1);
        Grid.SetRow(_status, 1);
        Grid.SetRow(_dropRing, 1);
        layout.Children.Add(_grid);
        layout.Children.Add(_empty);
        layout.Children.Add(_dropRing);
        layout.Children.Add(_status);
        Content = layout;

        layout.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            _dropRing.Visibility = e.Data.GetDataPresent(DataFormats.FileDrop) ? Visibility.Visible : Visibility.Collapsed;
            e.Handled = true;
        };
        layout.DragLeave += (_, _) => _dropRing.Visibility = Visibility.Collapsed;
        layout.Drop += (_, e) =>
        {
            _dropRing.Visibility = Visibility.Collapsed;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) _ = Import(files);
        };
        KeyDown += OnKey;
        void OnChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e) => Schedule();
        void OnImport() => ChooseFiles();
        void OnUnreadable() => library.Notice ??= "Some photos are in a format Windows can't read, such as HEIC. Their records are kept. Microsoft's HEIF Image Extensions can add it.";
        Loaded += (_, _) =>
        {
            library.PropertyChanged += OnChanged;
            context.Session.ImportRequested += OnImport;
            ThumbnailView.UnreadableSeen += OnUnreadable;
            Draw();
        };
        Unloaded += (_, _) =>
        {
            library.PropertyChanged -= OnChanged;
            context.Session.ImportRequested -= OnImport;
            ThumbnailView.UnreadableSeen -= OnUnreadable;
        };
        Draw();
    }

    public LibraryStore Library => _library;
    public Guid? Selection => _selection;

    void Schedule()
    {
        if (_renderPending) return;
        _renderPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            _renderPending = false;
            Draw();
        });
    }

    public IReadOnlyList<PhotoRecord> Images
    {
        get
        {
            var all = _library.VisiblePhotos.OrderByDescending(p => p.ImportedAt).ToList();
            return _filter switch
            {
                FilterBenchmarks => all.Where(p => _library.IsBenchmark(p.Id)).ToList(),
                FilterSet => CurrentSet is { } set ? all.Where(p => set.PhotoIDs.Contains(p.Id)).ToList() : [],
                _ => all,
            };
        }
    }

    SetRecord? CurrentSet => _filter is FilterSet f ? _library.Sets.FirstOrDefault(s => s.Id == f.Id) : null;

    void Draw()
    {
        DrawFilterRow();
        var images = Images;
        var width = _grid.ActualWidth > 0 ? _grid.ActualWidth : 1136;
        _columns = Math.Max(1, (int)((width + Spacing) / (TileMin + Spacing)));
        var signature = PhotoSignature(images) + "|" + _columns + "|" + _selection;
        if (signature != _gridSignature)
        {
            _gridSignature = signature;
            var rows = new List<PhotoRow>();
            for (var i = 0; i < images.Count; i += _columns)
            {
                var slice = images.Skip(i).Take(_columns).ToList();
                rows.Add(new PhotoRow(this, slice, PhotoSignature(slice) + _selection));
            }
            _grid.ItemsSource = rows;
        }
        _grid.Visibility = images.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _empty.Visibility = images.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (images.Count == 0)
        {
            var empty = Ui.Column(8, Ui.Text(_filter is FilterBenchmarks ? LineworkCopy.EmptyBenchmarks : LineworkCopy.EmptyLibrary, 26, Ui.Medium));
            if (CurrentSet is { } set) Ui.Add(empty, Ui.Text($"They'll go into “{set.Name}”.", 15, null, Tokens.Muted), 8);
            empty.Margin = new Thickness(SidePadding, 96, SidePadding, 0);
            _empty.Child = empty;
        }
        _status.Child = _importing || _status_ != null
            ? new Border
            {
                CornerRadius = new CornerRadius(18),
                Background = Tokens.Ground,
                BorderBrush = Tokens.Ink,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 8, 16, 8),
                Child = Ui.Text(_importing ? "Importing…" : _status_ ?? "", 13, Ui.Medium),
            }
            : null;
    }

    string PhotoSignature(IEnumerable<PhotoRecord> photos) =>
        string.Join(',', photos.Select(p => $"{p.Id}{p.Crop}{p.Rotation}{p.Flipped}{p.NoFigureFound}{_library.IsBenchmark(p.Id)}{p.FileName}"));

    void DrawFilterRow()
    {
        var signature = _filter + string.Join(',', _library.Sets.Select(s => s.Id + s.Name));
        if (signature == _filterSignature) return;
        _filterSignature = signature;
        _filterRow.Children.Clear();
        Ui.Add(_filterRow, Pill("All", new FilterAll()), 6);
        if (Linework.App.Library.FeatureFlags.BenchmarksAndFreehand) Ui.Add(_filterRow, Pill("Benchmarks", new FilterBenchmarks()), 6);
        foreach (var set in _library.Sets)
        {
            var pill = Pill(set.Name, new FilterSet(set.Id));
            var menu = new ContextMenu();
            menu.Items.Add(MenuItem("Rename…", () => Rename(set)));
            menu.Items.Add(MenuItem("Delete Set", () => DeleteSet(set.Id)));
            pill.ContextMenu = menu;
            Ui.Add(_filterRow, pill, 6);
        }
        var add = Ui.Capsule("+", CreateSet, 30);
        add.ToolTip = "New set";
        System.Windows.Automation.AutomationProperties.SetName(add, "New set");
        Ui.Add(_filterRow, add, 6);
        var import = Ui.Capsule("Import", ChooseFiles, 30);
        import.ToolTip = "Add photos to your library";
        Ui.Add(_filterRow, import, 6);
    }

    Button Pill(string title, LibraryFilter target) => Ui.Capsule(title, () =>
    {
        _filter = target;
        _filterSignature = "";
        Draw();
    }, 30, _filter == target);

    static MenuItem MenuItem(string title, Action click, bool enabled = true)
    {
        var item = new MenuItem { Header = title, IsEnabled = enabled };
        item.Click += (_, _) => click();
        return item;
    }

    void CreateSet()
    {
        var set = _library.CreateSet("New Set");
        _filter = new FilterSet(set.Id);
        _filterSignature = "";
        Rename(set);
    }

    void Rename(SetRecord set)
    {
        var name = AlertWindow.AskText("Set name", set.Name)?.Trim();
        if (!string.IsNullOrEmpty(name)) _library.RenameSet(set.Id, name);
        _filterSignature = "";
        Draw();
    }

    void DeleteSet(Guid id)
    {
        if (_filter == new FilterSet(id))
        {
            _filter = new FilterAll();
            _filterSignature = "";
        }
        _library.DeleteSet(id);
    }

    public void Select(Guid id)
    {
        _selection = id;
        Focus();
        Draw();
    }

    public void OpenPreview(Guid id)
    {
        var images = Images;
        _selection = id;
        var index = images.ToList().FindIndex(p => p.Id == id);
        new ImagePreviewWindow(_context, _library, images.Select(p => p.Id).ToList(), Math.Max(index, 0), selected =>
        {
            _selection = selected;
            Draw();
        }) { Owner = Window.GetWindow(this) }.ShowDialog();
        Draw();
    }

    public void EditCrop(PhotoRecord photo) => CropEditorWindow.Edit(_context, _library, photo, Window.GetWindow(this));

    public ContextMenu MenuFor(PhotoRecord photo)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("Preview", () => OpenPreview(photo.Id)));
        menu.Items.Add(MenuItem("Edit Crop…", () => EditCrop(photo)));
        var add = new MenuItem { Header = "Add to Set" };
        foreach (var set in _library.Sets) add.Items.Add(MenuItem(set.Name, () => _library.SetPhoto(photo.Id, set.Id, true), !set.PhotoIDs.Contains(photo.Id)));
        if (_library.Sets.Count == 0) add.Items.Add(MenuItem("No sets yet", () => { }, false));
        menu.Items.Add(add);
        if (CurrentSet is { } current) menu.Items.Add(MenuItem($"Remove from “{current.Name}”", () => _library.SetPhoto(photo.Id, current.Id, false)));
        if (Linework.App.Library.FeatureFlags.BenchmarksAndFreehand)
            menu.Items.Add(MenuItem(_library.IsBenchmark(photo.Id) ? "Unmark as Benchmark" : "Mark as Benchmark", () => _library.ToggleBenchmark(photo.Id)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Delete…", () =>
        {
            if (AlertWindow.Ask("Delete this image?", "It moves to the Recycle Bin. Its crop and any benchmark attempts are removed too.", "Delete", "Cancel") != 0) return;
            if (_selection == photo.Id) _selection = null;
            _library.DeletePhoto(photo.Id);
        }));
        return menu;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        var images = Images;
        if (images.Count == 0) return;
        var current = images.ToList().FindIndex(p => p.Id == _selection);
        switch (e.Key)
        {
            case Key.Space or Key.Enter:
                OpenPreview(images[Math.Max(current, 0)].Id);
                break;
            case Key.Left or Key.Right:
                var step = e.Key == Key.Left ? -1 : 1;
                _selection = images[Math.Min(Math.Max((current < 0 ? -step : current) + step, 0), images.Count - 1)].Id;
                Draw();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    void ChooseFiles()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Import Photos",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.webp;*.tif;*.tiff;*.gif;*.bmp|All files|*.*",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) _ = Import(dialog.FileNames);
    }

    async Task Import(IReadOnlyList<string> paths)
    {
        _importing = true;
        _status_ = null;
        Draw();
        var (imported, skipped) = await ImportService.ImportAsync(paths, _library, _context.App.Host, CurrentSet?.Id);
        _importing = false;
        var text = "Imported " + imported.ToString(CultureInfo.InvariantCulture);
        if (skipped > 0) text += $" · {skipped.ToString(CultureInfo.InvariantCulture)} skipped (not images)";
        _status_ = text;
        Draw();
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (_status_ == text)
        {
            _status_ = null;
            Draw();
        }
    }
}

/// <summary>One row of tiles, filled from its PhotoRow.</summary>
public sealed class PhotoRowView : Grid
{
    const double TileHeight = 180, Spacing = 16;

    public PhotoRowView()
    {
        Margin = new Thickness(0, 0, 0, Spacing);
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is PhotoRow row) Fill(row);
        };
    }

    void Fill(PhotoRow row)
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        var owner = row.Owner;
        var columns = Math.Max(1, (int)((owner.ActualWidth - 144 + Spacing) / (160 + Spacing)));
        for (var i = 0; i < columns; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition());
            if (i >= row.Photos.Count) continue;
            var tile = Tile(row.Photos[i], owner);
            tile.Margin = new Thickness(i == 0 ? 0 : Spacing / 2, 0, i == columns - 1 ? 0 : Spacing / 2, 0);
            SetColumn(tile, i);
            Children.Add(tile);
        }
    }

    static FrameworkElement Tile(PhotoRecord photo, LibraryView owner)
    {
        var library = owner.Library;
        var thumb = new ThumbnailView();
        thumb.Show(library.RequestFor(photo, 400));
        var grid = new Grid { Height = TileHeight, Background = Brushes.Transparent, ToolTip = "Double-click or press Space to preview" };
        grid.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = thumb });
        grid.Children.Add(new Border { CornerRadius = new CornerRadius(8), BorderBrush = Tokens.Hairline, BorderThickness = new Thickness(1) });
        // Sits just outside the tile, so it reads on any photo.
        if (owner.Selection == photo.Id)
            grid.Children.Add(new Border { CornerRadius = new CornerRadius(11), BorderBrush = Tokens.Ink, BorderThickness = new Thickness(1.5), Margin = new Thickness(-3) });
        var badges = Ui.Row(4).Align(HorizontalAlignment.Right, VerticalAlignment.Top).Margin(0, 6, 6);
        if (photo.NoFigureFound) Ui.Add(badges, Badge(Ui.Text("?", 11, Ui.SemiBold).Align(HorizontalAlignment.Center, VerticalAlignment.Center), "No figure found — crop by hand"), 4);
        if (library.IsBenchmark(photo.Id)) Ui.Add(badges, Badge(Ui.Icon(Ui.StarIcon, 11), "Benchmark"), 4);
        grid.Children.Add(badges);
        grid.ContextMenu = owner.MenuFor(photo);
        grid.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) owner.OpenPreview(photo.Id);
            else owner.Select(photo.Id);
            e.Handled = true;
        };
        return grid;
    }

    static Border Badge(FrameworkElement icon, string help) => new()
    {
        Width = 22,
        Height = 22,
        CornerRadius = new CornerRadius(11),
        Background = Tokens.Ground,
        BorderBrush = Tokens.Hairline,
        BorderThickness = new Thickness(1),
        Child = icon,
        ToolTip = help,
    };
}
