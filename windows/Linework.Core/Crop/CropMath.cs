namespace Linework.Core.Crop;

/// <summary>A crop as stored: normalized, top-left origin, applied after rotation and flip.</summary>
public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    public double MaxX => X + Width;
    public double MaxY => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;
}

public enum CropHandle { TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }

public static class CropHandles
{
    /// <summary>Which edges move: -1 = min edge, +1 = max edge, 0 = neither.</summary>
    public static (int X, int Y) Direction(this CropHandle handle) => handle switch
    {
        CropHandle.TopLeft => (-1, -1),
        CropHandle.Top => (0, -1),
        CropHandle.TopRight => (1, -1),
        CropHandle.Right => (1, 0),
        CropHandle.BottomRight => (1, 1),
        CropHandle.Bottom => (0, 1),
        CropHandle.BottomLeft => (-1, 1),
        _ => (-1, 0),
    };
}

/// <summary>Crop rectangles are normalized (0–1) with a top-left origin.</summary>
public static class CropMath
{
    public static readonly NormalizedRect Unit = new(0, 0, 1, 1);
    public const double MinimumCropSize = 0.05;

    /// <summary>Boxes with a bottom-left origin; flip to top-left.</summary>
    public static NormalizedRect FromVision(NormalizedRect r) => r with { Y = 1 - r.Y - r.Height };

    public static NormalizedRect? Union(IReadOnlyList<NormalizedRect> rects)
    {
        if (rects.Count == 0) return null;
        double minX = rects.Min(r => r.X), minY = rects.Min(r => r.Y), maxX = rects.Max(r => r.MaxX), maxY = rects.Max(r => r.MaxY);
        return new NormalizedRect(minX, minY, maxX - minX, maxY - minY);
    }

    public static NormalizedRect ClampedToUnit(NormalizedRect r)
    {
        var (x, w) = r.Width < 0 ? (r.X + r.Width, -r.Width) : (r.X, r.Width);
        var (y, h) = r.Height < 0 ? (r.Y + r.Height, -r.Height) : (r.Y, r.Height);
        double minX = Math.Max(x, 0), minY = Math.Max(y, 0), maxX = Math.Min(x + w, 1), maxY = Math.Min(y + h, 1);
        return maxX < minX || maxY < minY ? Unit : new NormalizedRect(minX, minY, maxX - minX, maxY - minY);
    }

    public static NormalizedRect Padded(NormalizedRect r, double fraction = 0.08)
    {
        double dx = r.Width * fraction, dy = r.Height * fraction;
        return ClampedToUnit(new NormalizedRect(r.X - dx, r.Y - dy, r.Width + 2 * dx, r.Height + 2 * dy));
    }

    /// <summary>The padded union of person boxes, already top-left; null when there are none.</summary>
    public static NormalizedRect? AutoCrop(IReadOnlyList<NormalizedRect> boxes) => Union(boxes) is { } u ? Padded(u) : null;

    /// <summary>Shrinks the rect around its center so its pixel aspect (w/h) equals aspect.</summary>
    public static NormalizedRect FitAspect(NormalizedRect r, double aspect, double imageAspect)
    {
        var pixelAspect = r.Width * imageAspect / r.Height;
        double w = r.Width, h = r.Height;
        if (pixelAspect > aspect) w = aspect * h / imageAspect;
        else h = w * imageAspect / aspect;
        return new NormalizedRect(r.MidX - w / 2, r.MidY - h / 2, w, h);
    }

    public static NormalizedRect MirroredX(NormalizedRect r) => r with { X = 1 - r.X - r.Width };

    /// <summary>Translates the rect, stopping at the unit rect's edges.</summary>
    public static NormalizedRect Moved(NormalizedRect r, double dx, double dy) =>
        r with { X = Math.Min(Math.Max(r.X + dx, 0), 1 - r.Width), Y = Math.Min(Math.Max(r.Y + dy, 0), 1 - r.Height) };

    /// <summary>Drags one handle by (dx, dy). With aspect (pixel w/h) set, the opposite side stays anchored
    /// and the box keeps that aspect, centered on the untouched axis for edge handles.</summary>
    public static NormalizedRect Resized(NormalizedRect r, CropHandle handle, double dx, double dy, double? aspect, double imageAspect, double minSize = MinimumCropSize)
    {
        var (hx, hy) = handle.Direction();
        double minX = r.X, maxX = r.MaxX, minY = r.Y, maxY = r.MaxY;
        if (hx < 0) minX = Math.Min(Math.Max(minX + dx, 0), maxX - minSize);
        if (hx > 0) maxX = Math.Max(Math.Min(maxX + dx, 1), minX + minSize);
        if (hy < 0) minY = Math.Min(Math.Max(minY + dy, 0), maxY - minSize);
        if (hy > 0) maxY = Math.Max(Math.Min(maxY + dy, 1), minY + minSize);
        if (aspect is not { } a) return new NormalizedRect(minX, minY, maxX - minX, maxY - minY);

        var k = a / imageAspect; // normalized width / height
        double w = maxX - minX, h = maxY - minY;
        if (hx != 0) h = w / k; else w = h * k;
        var maxW = hx > 0 ? 1 - r.X : hx < 0 ? r.MaxX : 2 * Math.Min(r.MidX, 1 - r.MidX);
        var maxH = hy > 0 ? 1 - r.Y : hy < 0 ? r.MaxY : 2 * Math.Min(r.MidY, 1 - r.MidY);
        if (w > maxW) { w = maxW; h = w / k; }
        if (h > maxH) { h = maxH; w = h * k; }
        if (w < minSize) { w = minSize; h = w / k; }
        if (h < minSize) { h = minSize; w = h * k; }
        var x = hx > 0 ? r.X : hx < 0 ? r.MaxX - w : r.MidX - w / 2;
        var y = hy > 0 ? r.Y : hy < 0 ? r.MaxY - h : r.MidY - h / 2;
        return new NormalizedRect(x, y, w, h);
    }

    /// <summary>Normalized top-left bounds of the set pixels in a mask whose row 0 is the top; null if none are set.</summary>
    public static NormalizedRect? MaskBounds(int width, int height, Func<int, int, bool> isSet)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (isSet(x, y))
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
        if (maxX < 0) return null;
        double w = width, h = height;
        return new NormalizedRect(minX / w, minY / h, (maxX - minX + 1) / w, (maxY - minY + 1) / h);
    }

    /// <summary>Pixels, rounded outward to whole numbers.</summary>
    public static NormalizedRect PixelRect(NormalizedRect norm, double imageWidth, double imageHeight)
    {
        double minX = Math.Floor(norm.X * imageWidth), minY = Math.Floor(norm.Y * imageHeight);
        double maxX = Math.Ceiling(norm.MaxX * imageWidth), maxY = Math.Ceiling(norm.MaxY * imageHeight);
        return new NormalizedRect(minX, minY, maxX - minX, maxY - minY);
    }
}
