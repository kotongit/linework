using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Linework.Core.Crop;
using Linework.Core.Library;

namespace Linework.App.Library;

/// <summary>Everything needed to render one reference image, captured as plain values so it can leave the UI thread.</summary>
public sealed record RenderRequest(string Path, int Rotation, bool Flipped, NormalizedRect Crop, int MaxPixel, string? ThumbnailSource = null, string? ThumbnailHash = null);

/// <summary>Reads images through Windows Imaging Component. Everything returned is frozen, so it can cross threads.</summary>
public static class ImageRendering
{
    /// <summary>Loads with EXIF orientation applied, downscaled to maxPixel on the long edge. Null if Windows can't read it.</summary>
    public static BitmapSource? Load(string path, int maxPixel = 3000)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            int width = frame.PixelWidth, height = frame.PixelHeight;
            var (rotation, flipped) = ExifOrientation.Transform(OrientationOf(frame));
            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (Math.Max(width, height) > maxPixel)
            {
                if (width >= height) image.DecodePixelWidth = maxPixel;
                else image.DecodePixelHeight = maxPixel;
            }
            image.EndInit();
            image.Freeze();
            return Oriented(image, rotation, flipped);
        }
        catch (Exception e) when (IsDecodeFailure(e))
        {
            return null;
        }
    }

    public static bool IsDecodeFailure(Exception e) =>
        e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or COMException or InvalidOperationException or FileFormatException;

    static int OrientationOf(BitmapFrame frame)
    {
        try
        {
            return frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("System.Photo.Orientation") is ushort value ? value : 1;
        }
        catch (Exception e) when (IsDecodeFailure(e))
        {
            return 1;
        }
    }

    /// <summary>Rotates clockwise by rotation degrees (0/90/180/270), then mirrors horizontally if flipped.</summary>
    public static BitmapSource Oriented(BitmapSource image, int rotation, bool flipped)
    {
        var r = (rotation % 360 + 360) % 360;
        if (r == 0 && !flipped) return image;
        BitmapSource result = image;
        if (r != 0) result = new TransformedBitmap(result, new RotateTransform(r));
        if (flipped) result = new TransformedBitmap(result, new ScaleTransform(-1, 1));
        return Solid(result);
    }

    public static BitmapSource Cropped(BitmapSource image, NormalizedRect rect)
    {
        var px = CropMath.PixelRect(CropMath.ClampedToUnit(rect), image.PixelWidth, image.PixelHeight);
        if (px.Width < 1 || px.Height < 1 || (px.X == 0 && px.Y == 0 && px.Width == image.PixelWidth && px.Height == image.PixelHeight)) return image;
        return Solid(new CroppedBitmap(image, new Int32Rect((int)px.X, (int)px.Y, (int)px.Width, (int)px.Height)));
    }

    /// <summary>Works it out now rather than when first drawn, and freezes it.</summary>
    static BitmapSource Solid(BitmapSource image)
    {
        var cached = new CachedBitmap(image, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        cached.Freeze();
        return cached;
    }

    /// <summary>Loads, orients and crops. Only the visible part is kept in memory.</summary>
    public static BitmapSource? Render(RenderRequest request)
    {
        if (Load(request.Path, request.MaxPixel) is not { } baseImage) return null;
        try
        {
            return Cropped(Oriented(baseImage, request.Rotation, request.Flipped), request.Crop);
        }
        catch (Exception e) when (IsDecodeFailure(e))
        {
            return null;
        }
    }

    public static (double Width, double Height)? OrientedSize(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            var (rotation, _) = ExifOrientation.Transform(OrientationOf(frame));
            return rotation is 90 or 270 ? (frame.PixelHeight, frame.PixelWidth) : (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception e) when (IsDecodeFailure(e))
        {
            return null;
        }
    }
}

/// <summary>400px thumbnails by hash, kept outside the library because they can always be made again.</summary>
public static class ThumbnailCache
{
    static readonly string Directory_ = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Linework", "Thumbnails");

    public static string PathFor(string hash) => Path.Combine(Directory_, hash + ".jpg");

    /// <summary>Makes the thumbnail if it isn't there. A failure just means the next render tries again.</summary>
    public static void Ensure(string hash, string source)
    {
        var target = PathFor(hash);
        if (File.Exists(target) || ImageRendering.Load(source, 400) is not { } image) return;
        try
        {
            Directory.CreateDirectory(Directory_);
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var memory = new MemoryStream();
            encoder.Save(memory);
            AtomicFile.Write(target, memory.ToArray());
        }
        catch (Exception e) when (ImageRendering.IsDecodeFailure(e))
        {
            // Nothing lost.
        }
    }
}

/// <summary>Process-wide cache of rendered images, so scrolling the grid or reopening a preview doesn't re-decode files.</summary>
public sealed class RenderCache
{
    public static readonly RenderCache Shared = new(256L * 1024 * 1024, 4);

    readonly long _limit;
    readonly SemaphoreSlim _gate;
    readonly LinkedList<(RenderRequest Key, BitmapSource Image, long Cost)> _order = new();
    readonly Dictionary<RenderRequest, LinkedListNode<(RenderRequest Key, BitmapSource Image, long Cost)>> _index = [];
    long _total;

    RenderCache(long limit, int concurrent)
    {
        _limit = limit;
        _gate = new SemaphoreSlim(concurrent);
    }

    public long TotalCost
    {
        get { lock (_order) return _total; }
    }

    public BitmapSource? Get(RenderRequest key)
    {
        lock (_order)
        {
            if (!_index.TryGetValue(key, out var node)) return null;
            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Image;
        }
    }

    void Insert(RenderRequest key, BitmapSource image)
    {
        var cost = (long)image.PixelWidth * image.PixelHeight * 4;
        lock (_order)
        {
            if (_index.ContainsKey(key)) return;
            _index[key] = _order.AddFirst((key, image, cost));
            _total += cost;
            while (_total > _limit && _order.Last is { } last && last != _order.First)
            {
                _total -= last.Value.Cost;
                _index.Remove(last.Value.Key);
                _order.RemoveLast();
            }
        }
    }

    /// <summary>Renders through the cache, off the UI thread, a few at a time.</summary>
    public async Task<BitmapSource?> RenderAsync(RenderRequest request, CancellationToken cancel = default)
    {
        if (Get(request) is { } hit) return hit;
        try
        {
            await _gate.WaitAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        try
        {
            if (cancel.IsCancellationRequested) return null;
            return await Task.Run(() =>
            {
                if (request.ThumbnailSource != null && request.ThumbnailHash != null) ThumbnailCache.Ensure(request.ThumbnailHash, request.ThumbnailSource);
                var image = ImageRendering.Render(request);
                if (image != null) Insert(request, image);
                return image;
            }, CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public static class RenderRequests
{
    /// <summary>useThumbnail renders from the cached 400px thumbnail; otherwise from the original. showCrop false shows the whole frame.</summary>
    public static RenderRequest RequestFor(this LibraryStore store, PhotoRecord photo, int maxPixel, bool? useThumbnail = null, bool showCrop = true)
    {
        var thumbnail = useThumbnail ?? maxPixel <= 400;
        var original = store.PhotoPath(photo);
        return new RenderRequest(thumbnail ? ThumbnailCache.PathFor(photo.Sha256) : original, photo.Rotation, photo.Flipped, showCrop ? photo.Crop : CropMath.Unit, maxPixel,
            thumbnail ? original : null, thumbnail ? photo.Sha256 : null);
    }
}
