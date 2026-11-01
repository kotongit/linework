using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Linework.App.Library;
using Linework.App.Theme;

namespace Linework.App.Views;

/// <summary>Renders a reference image with its orientation and crop applied, off the UI thread and through the cache.
/// A file Windows can't read keeps its place with a placeholder.</summary>
public sealed class ThumbnailView : UserControl
{
    readonly Image _image = new() { Stretch = Stretch.Uniform };
    RenderRequest? _request;
    CancellationTokenSource? _cancel;

    public static event Action? UnreadableSeen;

    public ThumbnailView()
    {
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Content = new Rectangle_();
        Unloaded += (_, _) => _cancel?.Cancel();
        Loaded += (_, _) =>
        {
            if (_request != null && _image.Source == null) _ = Load(_request);
        };
    }

    sealed class Rectangle_ : Border
    {
        public Rectangle_() => Background = Tokens.Hairline;
    }

    public void Show(RenderRequest request)
    {
        if (request == _request) return;
        _request = request;
        _cancel?.Cancel();
        // A cache hit draws immediately, without a placeholder flash.
        if (RenderCache.Shared.Get(request) is { } hit) Set(hit);
        else
        {
            _image.Source = null;
            Content = new Rectangle_();
            _ = Load(request);
        }
    }

    void Set(BitmapSource image)
    {
        _image.Source = image;
        Content = _image;
    }

    async Task Load(RenderRequest request)
    {
        _cancel = new CancellationTokenSource();
        var image = await RenderCache.Shared.RenderAsync(request, _cancel.Token);
        if (request != _request) return;
        if (image != null) Set(image);
        else if (_cancel is { IsCancellationRequested: false })
        {
            Content = new Border { Background = Tokens.Hairline, Child = Ui.Text("?", 20, Ui.Medium, Tokens.Muted).Align(HorizontalAlignment.Center, VerticalAlignment.Center) };
            ToolTip = "Windows can't read this file";
            UnreadableSeen?.Invoke();
        }
    }
}
