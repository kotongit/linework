using System.Text.Json;
using System.Text.Json.Serialization;
using Linework.Core.Crop;

namespace Linework.Core.Freehand;

/// <summary>As [x, y] in canvas.json, the way Swift encodes CGPoint.</summary>
[JsonConverter(typeof(Point2Converter))]
public readonly record struct Point2(double X, double Y);

/// <summary>As [w, h] in canvas.json, the way Swift encodes CGSize.</summary>
[JsonConverter(typeof(Size2Converter))]
public readonly record struct Size2(double Width, double Height);

static class JsonPair
{
    public static (double, double) Read(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray || !reader.Read()) throw new JsonException();
        var a = reader.GetDouble();
        if (!reader.Read()) throw new JsonException();
        var b = reader.GetDouble();
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray || !double.IsFinite(a) || !double.IsFinite(b)) throw new JsonException();
        return (a, b);
    }

    public static void Write(Utf8JsonWriter writer, double a, double b)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(a);
        writer.WriteNumberValue(b);
        writer.WriteEndArray();
    }
}

sealed class Point2Converter : JsonConverter<Point2>
{
    public override Point2 Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) { var (a, b) = JsonPair.Read(ref r); return new Point2(a, b); }
    public override void Write(Utf8JsonWriter w, Point2 v, JsonSerializerOptions o) => JsonPair.Write(w, v.X, v.Y);
}

sealed class Size2Converter : JsonConverter<Size2>
{
    public override Size2 Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) { var (a, b) = JsonPair.Read(ref r); return new Size2(a, b); }
    public override void Write(Utf8JsonWriter w, Size2 v, JsonSerializerOptions o) => JsonPair.Write(w, v.Width, v.Height);
}

/// <summary>[[x, y], [w, h]], the way Swift encodes CGRect.</summary>
sealed class CgRectConverter : JsonConverter<NormalizedRect>
{
    public override NormalizedRect Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        if (r.TokenType != JsonTokenType.StartArray || !r.Read()) throw new JsonException();
        var (x, y) = JsonPair.Read(ref r);
        if (!r.Read()) throw new JsonException();
        var (w, h) = JsonPair.Read(ref r);
        if (!r.Read() || r.TokenType != JsonTokenType.EndArray) throw new JsonException();
        return new NormalizedRect(x, y, w, h);
    }

    public override void Write(Utf8JsonWriter w, NormalizedRect v, JsonSerializerOptions o)
    {
        w.WriteStartArray();
        JsonPair.Write(w, v.X, v.Y);
        JsonPair.Write(w, v.Width, v.Height);
        w.WriteEndArray();
    }
}

/// <summary>One image placed on the Freehand canvas. Center is of the visible (cropped) rect, in canvas points. FullSize is the
/// uncropped image size, in canvas points. Rotation is degrees, clockwise. Crop is normalized, top-left origin, CropMath.Unit when whole.</summary>
[method: JsonConstructor]
public sealed record FreehandItem(Guid Id, string FileName, Point2 Center, Size2 FullSize, double Rotation, bool Flipped,
    [property: JsonConverter(typeof(CgRectConverter))] NormalizedRect Crop)
{
    public FreehandItem(Guid id, string fileName, Point2 center, Size2 fullSize) : this(id, fileName, center, fullSize, 0, false, CropMath.Unit) { }

    [JsonIgnore]
    public Size2 VisibleSize => new(FullSize.Width * Crop.Width, FullSize.Height * Crop.Height);

    /// <summary>The crop as it looks on the canvas, where a flipped image is mirrored. Crop itself stays in unflipped image space.</summary>
    [JsonIgnore]
    public NormalizedRect ShownCrop => Flipped ? CropMath.MirroredX(Crop) : Crop;

    /// <summary>The value for Crop, from a crop framed on the image as it looks on the canvas.</summary>
    public NormalizedRect StoredCrop(NormalizedRect shown) => Flipped ? CropMath.MirroredX(shown) : shown;
}

/// <summary>Pan and zoom: screen = canvas * zoom + offset.</summary>
public sealed record FreehandCamera(Point2 Offset, double Zoom)
{
    public const double MinZoom = 0.1, MaxZoom = 8;
    public static readonly FreehandCamera Home = new(default, 1);

    public Point2 ToScreen(Point2 p) => new(p.X * Zoom + Offset.X, p.Y * Zoom + Offset.Y);

    public Point2 ToCanvas(Point2 p) => new((p.X - Offset.X) / Zoom, (p.Y - Offset.Y) / Zoom);

    /// <summary>Zooms by the factor, clamped, keeping the canvas point under screenPoint fixed.</summary>
    public FreehandCamera Zoomed(double factor, Point2 screenPoint)
    {
        var target = Math.Min(Math.Max(Zoom * factor, MinZoom), MaxZoom);
        var ratio = target / Zoom;
        return new FreehandCamera(new Point2(screenPoint.X - (screenPoint.X - Offset.X) * ratio, screenPoint.Y - (screenPoint.Y - Offset.Y) * ratio), target);
    }

    public FreehandCamera Panned(Size2 delta) => new(new Point2(Offset.X + delta.Width, Offset.Y + delta.Height), Zoom);

    /// <summary>A camera that cannot produce NaN: finite zoom is clamped; any non-finite offset or zoom gives Home.</summary>
    [JsonIgnore]
    public FreehandCamera Sanitized =>
        double.IsFinite(Offset.X) && double.IsFinite(Offset.Y) && double.IsFinite(Zoom)
            ? new FreehandCamera(Offset, Math.Min(Math.Max(Zoom, MinZoom), MaxZoom))
            : Home;
}

/// <summary>Items are back to front.</summary>
public sealed record FreehandDocument(int Version = FreehandDocument.CurrentVersion, bool Greyscale = false, FreehandCamera? Camera = null, IReadOnlyList<FreehandItem>? Items = null)
{
    public const int CurrentVersion = 1;
    public static readonly FreehandDocument Empty = new();

    public FreehandCamera Camera { get; init; } = Camera ?? FreehandCamera.Home;
    public IReadOnlyList<FreehandItem> Items { get; init; } = Items ?? [];
}

/// <summary>Turns wheel messages into a pan. A mouse sends whole notches of 120; a precision touchpad sends small steps, which pass straight through.</summary>
public static class CanvasWheel
{
    public static Size2 Vertical(int delta) => new(0, Distance(delta));
    /// <summary>Positive means scrolling right, which moves the canvas left.</summary>
    public static Size2 Horizontal(int delta) => new(-Distance(delta), 0);
    public static Size2 ShiftVertical(int delta) => new(Distance(delta), 0);

    /// <summary>The signed high word of a WM_MOUSEHWHEEL wParam.</summary>
    public static int DeltaFrom(long wParam) => (short)((wParam >> 16) & 0xFFFF);

    /// <summary>The signed screen point in a mouse message's lParam.</summary>
    public static (int X, int Y) PointFrom(long lParam) => ((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));

    static double Distance(int delta) => delta * (delta % 120 == 0 ? NotchPoints / 120.0 : FinePointsPerUnit);

    const double NotchPoints = 30, FinePointsPerUnit = 0.8;
}

public enum Corner { TopLeft, TopRight, BottomRight, BottomLeft }

public static class FreehandGeometry
{
    const double FitMargin = 40;
    public const int StandardDecodePixels = 1600, SharpDecodePixels = 3000;
    const double SharpDecodeThreshold = 1200;
    public const double MinimumLongEdge = 32;
    const double RotateHandleOffset = 28;

    /// <summary>Camera that shows every item with a 40pt margin, never zoomed past 100%.</summary>
    public static FreehandCamera Fitted(IReadOnlyList<FreehandItem> items, Size2 viewport)
    {
        var points = items.SelectMany(Corners).ToList();
        if (points.Count == 0) return FreehandCamera.Home;
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        double w = Math.Max(maxX - minX, 1e-6), h = Math.Max(maxY - minY, 1e-6);
        var raw = Math.Min(Math.Min((viewport.Width - 2 * FitMargin) / w, (viewport.Height - 2 * FitMargin) / h), 1);
        var zoom = Math.Min(Math.Max(raw, FreehandCamera.MinZoom), FreehandCamera.MaxZoom);
        double midX = (minX + maxX) / 2, midY = (minY + maxY) / 2;
        return new FreehandCamera(new Point2(viewport.Width / 2 - midX * zoom, viewport.Height / 2 - midY * zoom), zoom);
    }

    /// <summary>Visible-rect corners (TL, TR, BR, BL) rotated clockwise about the item center, in canvas space.</summary>
    public static IReadOnlyList<Point2> Corners(FreehandItem item)
    {
        double hw = item.VisibleSize.Width / 2, hh = item.VisibleSize.Height / 2;
        var theta = item.Rotation * Math.PI / 180;
        double c = Math.Cos(theta), s = Math.Sin(theta);
        return new (double X, double Y)[] { (-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh) }
            .Select(p => new Point2(item.Center.X + p.X * c - p.Y * s, item.Center.Y + p.X * s + p.Y * c)).ToList();
    }

    /// <summary>Bounding box of the item's visible rect, rotation included, in screen points.</summary>
    public static NormalizedRect ScreenBounds(FreehandItem item, FreehandCamera camera)
    {
        var points = Corners(item).Select(camera.ToScreen).ToList();
        double minX = points.Min(p => p.X), minY = points.Min(p => p.Y);
        return new NormalizedRect(minX, minY, points.Max(p => p.X) - minX, points.Max(p => p.Y) - minY);
    }

    public static bool IsOnScreen(FreehandItem item, FreehandCamera camera, Size2 viewport)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0) return false;
        var b = ScreenBounds(item, camera);
        return b.X < viewport.Width && b.MaxX > 0 && b.Y < viewport.Height && b.MaxY > 0;
    }

    /// <summary>How large to decode an item. The whole image is decoded and then cropped, so a tight crop needs the
    /// full image's size on screen. The sharp size is for what can be seen: it costs too much to keep for the rest.</summary>
    public static int DecodePixels(FreehandItem item, FreehandCamera camera, Size2 viewport)
    {
        var fullLongEdge = Math.Max(item.FullSize.Width, item.FullSize.Height) * camera.Zoom;
        return fullLongEdge > SharpDecodeThreshold && IsOnScreen(item, camera, viewport) ? SharpDecodePixels : StandardDecodePixels;
    }

    static Point2 Rotate(Point2 v, double degrees)
    {
        var t = degrees * Math.PI / 180;
        double c = Math.Cos(t), s = Math.Sin(t);
        return new Point2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    /// <summary>Topmost item whose visible rect contains the point (items are back to front).</summary>
    public static Guid? Hit(IReadOnlyList<FreehandItem> items, Point2 canvasPoint)
    {
        foreach (var item in items.Reverse())
        {
            var local = Rotate(new Point2(canvasPoint.X - item.Center.X, canvasPoint.Y - item.Center.Y), -item.Rotation);
            if (Math.Abs(local.X) <= item.VisibleSize.Width / 2 && Math.Abs(local.Y) <= item.VisibleSize.Height / 2) return item.Id;
        }
        return null;
    }

    public static IReadOnlyList<FreehandItem> BroughtToFront(IReadOnlyList<FreehandItem> items, Guid id)
    {
        var moved = items.FirstOrDefault(i => i.Id == id);
        return moved == null ? items : [.. items.Where(i => i.Id != id), moved];
    }

    /// <summary>Scales proportionally by dragging the corner to the point; the opposite corner stays fixed.</summary>
    public static FreehandItem Resized(FreehandItem item, Corner corner, Point2 canvasPoint)
    {
        double hw = item.VisibleSize.Width / 2, hh = item.VisibleSize.Height / 2;
        (double X, double Y)[] signs = [(-1, -1), (1, -1), (1, 1), (-1, 1)];
        var (sx, sy) = signs[(int)corner];
        var dragged = new Point2(sx * hw, sy * hh); // local, relative to center
        var opposite = Rotate(new Point2(-dragged.X, -dragged.Y), item.Rotation);
        var oppositeCanvas = new Point2(item.Center.X + opposite.X, item.Center.Y + opposite.Y);
        var pointer = Rotate(new Point2(canvasPoint.X - oppositeCanvas.X, canvasPoint.Y - oppositeCanvas.Y), -item.Rotation);
        var diagonal = new Point2(2 * dragged.X, 2 * dragged.Y);
        var lengthSquared = diagonal.X * diagonal.X + diagonal.Y * diagonal.Y;
        if (lengthSquared <= 0) return item;
        var projected = (pointer.X * diagonal.X + pointer.Y * diagonal.Y) / lengthSquared;
        var longEdge = Math.Max(item.VisibleSize.Width, item.VisibleSize.Height);
        var scale = Math.Max(projected, MinimumLongEdge / longEdge);
        var half = Rotate(new Point2(diagonal.X * scale / 2, diagonal.Y * scale / 2), item.Rotation);
        return item with
        {
            FullSize = new Size2(item.FullSize.Width * scale, item.FullSize.Height * scale),
            Center = new Point2(oppositeCanvas.X + half.X, oppositeCanvas.Y + half.Y),
        };
    }

    /// <summary>Points the item's up direction at the pointer; snaps to 15 degree steps when snapping.</summary>
    public static FreehandItem Rotated(FreehandItem item, Point2 pointer, bool snapping)
    {
        var degrees = Math.Atan2(pointer.Y - item.Center.Y, pointer.X - item.Center.X) * 180 / Math.PI + 90;
        if (snapping) degrees = Math.Round(degrees / 15, MidpointRounding.AwayFromZero) * 15;
        degrees %= 360;
        if (degrees < 0) degrees += 360;
        return item with { Rotation = degrees };
    }

    public static FreehandItem Flipped(FreehandItem item) => item with { Flipped = !item.Flipped };

    /// <summary>Changes the crop while keeping the image content where it is on the canvas.</summary>
    public static FreehandItem Cropped(FreehandItem item, NormalizedRect crop)
    {
        Point2 Offset(NormalizedRect c)
        {
            var x = (c.MidX - 0.5) * item.FullSize.Width;
            return new Point2(item.Flipped ? -x : x, (c.MidY - 0.5) * item.FullSize.Height);
        }
        Point2 old = Offset(item.Crop), fresh = Offset(crop);
        var shift = Rotate(new Point2(fresh.X - old.X, fresh.Y - old.Y), item.Rotation);
        return item with { Crop = crop, Center = new Point2(item.Center.X + shift.X, item.Center.Y + shift.Y) };
    }

    /// <summary>Canvas point 28 screen points above the visible top edge's midpoint, along the item's up direction.</summary>
    public static Point2 RotateHandle(FreehandItem item, double zoom)
    {
        var r = Rotate(new Point2(0, -(item.VisibleSize.Height / 2 + RotateHandleOffset / zoom)), item.Rotation);
        return new Point2(item.Center.X + r.X, item.Center.Y + r.Y);
    }
}
