using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Linework.App.Library;
using Linework.App.Theme;
using Linework.Core.Library;
using Microsoft.Win32;

namespace Linework.App.Views;

/// <summary>Shown after a benchmark pose; records the attempt with or without a drawing photo.</summary>
public sealed class AttemptSheet : GlassWindow
{
    readonly LibraryStore _library;
    readonly PhotoRecord _photo;
    readonly int _number;
    readonly Guid _sessionId;
    readonly ContentControl _dropContent = new();
    readonly TextBlock _error = Ui.Text("", 14, Ui.Medium);
    Button _save = null!;
    string? _drawing;

    public AttemptSheet(LineworkContext context, LibraryStore library, PhotoRecord photo, int number, Guid sessionId) : base(context.Settings)
    {
        _library = library;
        _photo = photo;
        _number = number;
        _sessionId = sessionId;
        Title = $"Benchmark #{number} done";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) Save(null);
            else if (e.Key == System.Windows.Input.Key.Enter && _drawing != null) Save(_drawing);
        };
        Content = Body();
    }

    public FrameworkElement Body()
    {
        var thumb = new ThumbnailView { Width = 220, Height = 280 };
        thumb.Show(_library.RequestFor(_photo, 400));
        _save = Ui.Capsule("Save", () => Save(_drawing), 30, true);
        _save.IsEnabled = false;
        var skip = Ui.Capsule("Skip", Close, 30);
        skip.ToolTip = "Don’t count this attempt; the benchmark stays due";
        var end = Ui.Row(8, skip, Ui.Capsule("Later", () => Save(null), 30), _save).Align(HorizontalAlignment.Right);
        var buttons = new DockPanel();
        var choose = Ui.Capsule("Choose…", Choose, 30);
        DockPanel.SetDock(choose, Dock.Left);
        buttons.Children.Add(choose);
        buttons.Children.Add(end);
        DrawDrop();
        var panel = Ui.Column(20,
            Ui.Text($"Benchmark #{_number} done", 26, Ui.Medium),
            Ui.Row(16, thumb, DropZone()),
            _error,
            buttons);
        panel.Margin = new Thickness(28);
        return panel;
    }

    FrameworkElement DropZone()
    {
        var ring = new Rectangle { Stroke = Tokens.Muted, StrokeThickness = 1, StrokeDashArray = [4, 4], RadiusX = 8, RadiusY = 8 };
        var grid = new Grid { Width = 220, Height = 280, AllowDrop = true, Background = Brushes.Transparent };
        grid.Children.Add(ring);
        grid.Children.Add(_dropContent);
        grid.DragOver += (_, e) =>
        {
            ring.Stroke = Tokens.Ink;
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        grid.DragLeave += (_, _) => ring.Stroke = Tokens.Muted;
        grid.Drop += (_, e) =>
        {
            ring.Stroke = Tokens.Muted;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) SetDrawing(files[0]);
        };
        return grid;
    }

    void DrawDrop()
    {
        if (_drawing != null)
        {
            _dropContent.Content = new Image { Source = ImageRendering.Load(_drawing, 600), Stretch = Stretch.Uniform, Margin = new Thickness(8) };
            return;
        }
        var text = Ui.Text("Drop a photo of your drawing", 14, null, Tokens.Muted, 190);
        text.TextAlignment = TextAlignment.Center;
        var plus = Ui.Text("+", 16, null, Tokens.Muted).Align(HorizontalAlignment.Center);
        _dropContent.Content = Ui.Column(8, plus, text).Align(HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    void SetDrawing(string path)
    {
        _drawing = path;
        _save.IsEnabled = true;
        DrawDrop();
    }

    void Choose()
    {
        var dialog = new OpenFileDialog { Title = "Choose a photo of your drawing", Filter = "Images|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.webp;*.tif;*.tiff;*.gif;*.bmp|All files|*.*" };
        if (dialog.ShowDialog(this) == true) SetDrawing(dialog.FileName);
    }

    void Save(string? drawing)
    {
        try
        {
            _library.AddAttempt(_photo.Id, _sessionId, drawing);
            Close();
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or LibraryUnavailableException)
        {
            _error.Text = "That file couldn’t be read as an image.";
        }
    }
}
