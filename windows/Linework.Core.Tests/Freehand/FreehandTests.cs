using Linework.Core.Crop;
using Linework.Core.Freehand;
using Geo = Linework.Core.Freehand.FreehandGeometry;

namespace Linework.Core.Tests;

public class FreehandFilesTests
{
    static (string Root, FreehandFiles Files) Temp()
    {
        var root = Lib.Temp();
        var files = new FreehandFiles(root);
        files.PrepareImagesDir();
        return (root, files);
    }

    static FreehandItem Item(FreehandFiles files, bool write = true, Point2 center = default)
    {
        var name = Guid.NewGuid() + ".png";
        if (write) File.WriteAllBytes(files.ImagePath(name), [1, 2, 3]);
        return new FreehandItem(Guid.NewGuid(), name, center, new Size2(100, 50));
    }

    static void Same(FreehandDocument a, FreehandDocument b)
    {
        Assert.Equal(a.Greyscale, b.Greyscale);
        Assert.Equal(a.Camera, b.Camera);
        Assert.Equal(a.Items, b.Items);
    }

    [Fact]
    public void LoadMissingIsEmpty()
    {
        var (_, files) = Temp();
        Same(FreehandDocument.Empty, files.Load());
        Assert.False(files.HasDamagedFile);
    }

    [Fact]
    public void SaveLoadRoundTrip()
    {
        var (_, files) = Temp();
        var b = Item(files, center: new Point2(-20, 30)) with { Rotation = 45, Flipped = true };
        var doc = new FreehandDocument(1, true, new FreehandCamera(new Point2(12, -7), 2.5), [Item(files, center: new Point2(5, 6)), b]);
        files.Save(doc);
        Same(doc, files.Load());
    }

    [Fact]
    public void DamagedFileIsSetAside()
    {
        var (root, files) = Temp();
        var item = Item(files);
        File.WriteAllText(Path.Combine(root, "canvas.json"), "garbage{");
        Same(FreehandDocument.Empty, files.Load());
        Assert.Equal("garbage{", File.ReadAllText(Path.Combine(root, "canvas.damaged.json")));
        Assert.False(File.Exists(Path.Combine(root, "canvas.json")));
        Assert.True(File.Exists(files.ImagePath(item.FileName)));
        Assert.True(files.HasDamagedFile);
    }

    [Fact]
    public void NewerVersionIsSetAside()
    {
        var (root, files) = Temp();
        var item = Item(files);
        var text = $$"""{"version":99,"greyscale":false,"camera":{"offset":[0,0],"zoom":1},"items":[]}""";
        File.WriteAllText(Path.Combine(root, "canvas.json"), text);
        Same(FreehandDocument.Empty, files.Load());
        Assert.Equal(text, File.ReadAllText(Path.Combine(root, "canvas.damaged.json")));
        Assert.True(File.Exists(files.ImagePath(item.FileName)));
    }

    [Fact]
    public void DamagedKeepsEarlierDamaged()
    {
        var (root, files) = Temp();
        File.WriteAllText(Path.Combine(root, "canvas.damaged.json"), "first");
        File.WriteAllText(Path.Combine(root, "canvas.json"), "second");
        files.Load();
        Assert.Equal("first", File.ReadAllText(Path.Combine(root, "canvas.damaged.json")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(root, "canvas.damaged 2.json")));
    }

    [Fact]
    public void ItemWithMissingFileIsDropped()
    {
        var (_, files) = Temp();
        var kept = Item(files);
        files.Save(new FreehandDocument(Items: [Item(files, write: false), kept]));
        Assert.Equal([kept], files.Load().Items);
    }

    [Fact]
    public void OrphansAreDeletedAfterGoodLoad()
    {
        var (_, files) = Temp();
        var kept = Item(files);
        var orphan = Item(files);
        files.Save(new FreehandDocument(Items: [kept]));
        Assert.Equal([kept], files.Load().Items);
        Assert.True(File.Exists(files.ImagePath(kept.FileName)));
        Assert.False(File.Exists(files.ImagePath(orphan.FileName)));
    }

    [Fact]
    public void DamagedFileBlocksOrphanCleanupLater()
    {
        var (root, files) = Temp();
        var old = Item(files);
        File.WriteAllText(Path.Combine(root, "canvas.json"), "garbage");
        files.Load();
        var fresh = Item(files);
        files.Save(new FreehandDocument(Items: [fresh]));
        Assert.Equal([fresh], files.Load().Items);
        Assert.True(File.Exists(files.ImagePath(old.FileName)));
    }

    [Fact]
    public void NfdImageOnDiskKeepsItsItem()
    {
        var (_, files) = Temp();
        File.WriteAllBytes(Path.Combine(files.ImagesDir, "Jose\u0301.png"), [1]);
        var item = new FreehandItem(Guid.NewGuid(), "Jos\u00E9.png", default, new Size2(100, 50));
        files.Save(new FreehandDocument(Items: [item]));
        Assert.Equal([item], files.Load().Items);
        Assert.Single(Directory.GetFiles(files.ImagesDir));
        Assert.Equal([1], File.ReadAllBytes(files.ImagePath(item.FileName)));
    }

    [Fact]
    public void RemovingOneOfTwoCopiesKeepsTheOther()
    {
        var (_, files) = Temp();
        var a = Item(files, write: false);
        var b = Item(files, write: false);
        File.WriteAllBytes(files.ImagePath(a.FileName), [9]);
        File.WriteAllBytes(files.ImagePath(b.FileName), [9]);
        files.Save(new FreehandDocument(Items: [a, b]));
        files.RemoveImage(a.FileName);
        Assert.False(File.Exists(files.ImagePath(a.FileName)));
        Assert.Equal([b], files.Load().Items);
    }

    [Fact]
    public void SaveLeavesNoTempFiles()
    {
        var (root, files) = Temp();
        files.Save(new FreehandDocument(Items: [Item(files)]));
        files.Save(new FreehandDocument(Greyscale: true));
        Assert.Equal(["Images", "canvas.json"], Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void LoadSanitizesCamera()
    {
        var (root, files) = Temp();
        files.Save(new FreehandDocument(Camera: new FreehandCamera(new Point2(3, 4), 100)));
        Assert.Equal(new FreehandCamera(new Point2(3, 4), 8), files.Load().Camera);
        files.Save(new FreehandDocument(Camera: new FreehandCamera(new Point2(3, 4), 0.001)));
        Assert.Equal(0.1, files.Load().Camera.Zoom);

        File.WriteAllText(Path.Combine(root, "canvas.json"), """{"version":1,"greyscale":false,"camera":{"offset":[0,0],"zoom":1e999},"items":[]}""");
        Same(FreehandDocument.Empty, files.Load());
        Assert.True(files.HasDamagedFile);

        Assert.Equal(FreehandCamera.Home, new FreehandCamera(new Point2(double.NaN, 0), 1).Sanitized);
        Assert.Equal(FreehandCamera.Home, new FreehandCamera(default, double.NaN).Sanitized);
        Assert.Equal(FreehandCamera.Home, new FreehandCamera(default, double.PositiveInfinity).Sanitized);
        Assert.Equal(FreehandCamera.Home, new FreehandCamera(new Point2(0, double.NegativeInfinity), 2).Sanitized);
    }

    [Fact]
    public void AnUnreadableCanvasIsReportedNotEmptied()
    {
        if (OperatingSystem.IsWindows()) return; // No POSIX modes there.
        var (root, files) = Temp();
        var url = Path.Combine(root, "canvas.json");
        files.Save(FreehandDocument.Empty);
        File.SetUnixFileMode(url, UnixFileMode.None);
        try
        {
            Assert.Throws<Library.UnreadableException>(() => files.Load());
            Assert.True(File.Exists(url));
        }
        finally { File.SetUnixFileMode(url, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    [Fact]
    public void ABadCanvasThatCannotBeSetAsideIsReportedNotEmptied()
    {
        if (OperatingSystem.IsWindows()) return; // No POSIX modes there.
        var (root, files) = Temp();
        var item = Item(files);
        File.WriteAllText(Path.Combine(root, "canvas.json"), "garbage");
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.Throws<Library.UnreadableException>(() => files.Load());
            Assert.True(File.Exists(files.ImagePath(item.FileName)));
        }
        finally { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    [Fact]
    public void AskingForTheImagesPathCreatesNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "gone-" + Guid.NewGuid());
        _ = new FreehandFiles(Path.Combine(root, "Canvases/A")).ImagesDir;
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void SavingCantRecreateAMissingLibrary()
    {
        var root = Path.Combine(Path.GetTempPath(), "gone-" + Guid.NewGuid());
        Assert.ThrowsAny<IOException>(() => new FreehandFiles(Path.Combine(root, "Canvases/A")).Save(FreehandDocument.Empty));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void JsonMatchesTheMacSample()
    {
        var doc = FreehandFiles.Decode(File.ReadAllBytes(Repo.Fixture("canvas-sample.json")));
        var again = FreehandFiles.Decode(FreehandFiles.Encode(doc));
        Same(doc, again);
        Assert.Equal(new NormalizedRect(0.1, 0.2, 0.5, 0.75), again.Items[0].Crop);
        Assert.Equal("00000000-AAAA-4BBB-8CCC-000000000060", again.Items[0].Id.ToString("D").ToUpperInvariant());
        var text = System.Text.Encoding.UTF8.GetString(FreehandFiles.Encode(doc));
        Assert.Contains("\"crop\":[[0.1,0.2],[0.5,0.75]]", text);
        Assert.Contains("\"center\":[100,200.25]", text);
        Assert.Contains("00000000-AAAA-4BBB-8CCC-000000000060", text);
    }
}

public class CanvasWheelTests
{
    [Fact]
    public void AMouseNotchMovesThirtyPoints()
    {
        Assert.Equal(new Size2(0, 30), CanvasWheel.Vertical(120));
        Assert.Equal(new Size2(0, -60), CanvasWheel.Vertical(-240));
    }

    [Fact]
    public void ATouchpadsFineStepsStayFine()
    {
        Assert.Equal(new Size2(0, 3 * 0.8), CanvasWheel.Vertical(3));
        Assert.Equal(new Size2(-7 * 0.8, 0), CanvasWheel.Horizontal(7));
    }

    [Fact]
    public void ScrollingRightMovesTheCanvasLeft()
    {
        Assert.True(CanvasWheel.Horizontal(120).Width < 0);
        Assert.Equal(new Size2(30, 0), CanvasWheel.ShiftVertical(120));
    }

    [Theory]
    [InlineData(0x00780000L, 120), InlineData(0xFF880000L, -120), InlineData(0x00030008L, 3), InlineData(unchecked((long)0xFFFFFFFFFFF90000UL), -7)]
    public void TheDeltaIsTheMessagesHighWord(long wParam, int delta) => Assert.Equal(delta, CanvasWheel.DeltaFrom(wParam));

    [Theory]
    [InlineData(0x01F40320L, 800, 500), InlineData(0xFFF6FFFEL, -2, -10)]
    public void ThePointIsSigned(long lParam, int x, int y) => Assert.Equal((x, y), CanvasWheel.PointFrom(lParam));
}

public class FreehandCanvasTests
{
    internal static void Near(Point2 a, Point2 b, double tol = 1e-6) => Assert.True(Math.Abs(a.X - b.X) < tol && Math.Abs(a.Y - b.Y) < tol, $"{a} vs {b}");
    internal static FreehandItem Make(double w, double h, double cx = 0, double cy = 0, double rotation = 0, bool flipped = false, NormalizedRect? crop = null) =>
        new(Guid.NewGuid(), "a.png", new Point2(cx, cy), new Size2(w, h), rotation, flipped, crop ?? CropMath.Unit);

    [Fact]
    public void ScreenCanvasRoundTrip()
    {
        foreach (var zoom in new[] { 0.1, 1, 8 })
            foreach (var offset in new[] { default, new Point2(-300, 120) })
            {
                var c = new FreehandCamera(offset, zoom);
                var p = new Point2(123.5, -77.25);
                Near(c.ToCanvas(c.ToScreen(p)), p);
            }
    }

    [Fact]
    public void ZoomKeepsFocusFixed()
    {
        var c = new FreehandCamera(new Point2(-300, 120), 1.3);
        var f = new Point2(400, 250);
        Near(c.Zoomed(1.7, f).ToCanvas(f), c.ToCanvas(f));
    }

    [Fact]
    public void ZoomClamps()
    {
        var c = new FreehandCamera(new Point2(10, 20), 1);
        var f = new Point2(400, 250);
        Assert.Equal(8, c.Zoomed(100, f).Zoom);
        Assert.Equal(0.1, c.Zoomed(0.001, f).Zoom);
        Near(c.Zoomed(100, f).ToCanvas(f), c.ToCanvas(f));
    }

    [Fact]
    public void PanMovesOffset() => Assert.Equal(new Point2(5, -3), FreehandCamera.Home.Panned(new Size2(5, -3)).Offset);

    [Fact]
    public void VisibleSizeFollowsCrop() => Assert.Equal(new Size2(200, 100), Make(400, 200, crop: new NormalizedRect(0, 0, 0.5, 0.5)).VisibleSize);

    [Fact]
    public void FitEmptyIsHome() => Assert.Equal(FreehandCamera.Home, Geo.Fitted([], new Size2(1000, 800)));

    [Fact]
    public void FitNeverOver100()
    {
        var c = Geo.Fitted([Make(10, 10)], new Size2(1000, 800));
        Assert.Equal(1, c.Zoom);
        Near(c.ToScreen(default), new Point2(500, 400));
    }

    [Fact]
    public void FitUsesMargin()
    {
        var c = Geo.Fitted([Make(2000, 1000)], new Size2(1000, 800));
        Assert.True(Math.Abs(c.Zoom - 0.46) < 1e-9);
        Near(c.ToScreen(default), new Point2(500, 400));
    }

    [Fact]
    public void FitCountsRotation() => Assert.True(Math.Abs(Geo.Fitted([Make(1000, 100, rotation: 90)], new Size2(1000, 800)).Zoom - 0.72) < 1e-9);

    [Fact]
    public void CornersRotateClockwise()
    {
        var corners = Geo.Corners(Make(100, 100, rotation: 90));
        Assert.Equal(4, corners.Count);
        Near(corners[0], new Point2(50, -50));
    }
}

public class FreehandEditTests
{
    static void Near(Point2 a, Point2 b) => FreehandCanvasTests.Near(a, b, 0.001);
    static FreehandItem Make(double w, double h, double cx = 0, double cy = 0, double rotation = 0, bool flipped = false, NormalizedRect? crop = null) =>
        FreehandCanvasTests.Make(w, h, cx, cy, rotation, flipped, crop);
    static Point2 Rot(Point2 v, double degrees)
    {
        var t = degrees * Math.PI / 180;
        return new Point2(v.X * Math.Cos(t) - v.Y * Math.Sin(t), v.X * Math.Sin(t) + v.Y * Math.Cos(t));
    }
    static readonly Size2 Viewport = new(1000, 700);

    [Fact]
    public void HitPicksTopmost()
    {
        var a = Make(100, 100);
        var b = Make(100, 100, 20);
        Assert.Equal(b.Id, Geo.Hit([a, b], new Point2(10, 0)));
        Assert.Equal(a.Id, Geo.Hit([b, a], new Point2(10, 0)));
        Assert.Null(Geo.Hit([a, b], new Point2(500, 0)));
    }

    [Fact]
    public void HitMissesRotatedCorner()
    {
        var a = Make(200, 20, rotation: 45);
        Assert.Equal(a.Id, Geo.Hit([a], new Point2(63.6, 63.6)));
        Assert.Null(Geo.Hit([a], new Point2(90, 0)));
    }

    [Fact]
    public void HitUsesVisibleCrop()
    {
        var a = Make(100, 100, crop: new NormalizedRect(0, 0, 0.5, 1));
        Assert.Equal(a.Id, Geo.Hit([a], new Point2(20, 0)));
        Assert.Null(Geo.Hit([a], new Point2(40, 0)));
    }

    [Fact]
    public void FrontKeepsOthersOrder()
    {
        FreehandItem a = Make(10, 10), b = Make(10, 10), c = Make(10, 10);
        Assert.Equal([b.Id, c.Id, a.Id], Geo.BroughtToFront([a, b, c], a.Id).Select(i => i.Id));
    }

    [Fact]
    public void ResizeKeepsProportions()
    {
        foreach (var rotation in new[] { 0.0, 37, 180 })
            foreach (var corner in Enum.GetValues<Corner>())
            {
                var it = Make(300, 200, 50, -30, rotation, crop: new NormalizedRect(0.1, 0.2, 0.6, 0.5));
                var pts = Geo.Corners(it);
                var target = pts[(int)corner];
                var opp = pts[((int)corner + 2) % 4];
                var dir = new Point2(target.X - opp.X, target.Y - opp.Y);
                var r = Geo.Resized(it, corner, new Point2(opp.X + dir.X * 1.4 + dir.Y * 0.05, opp.Y + dir.Y * 1.4 - dir.X * 0.05));
                Assert.True(Math.Abs(r.VisibleSize.Width / r.VisibleSize.Height - it.VisibleSize.Width / it.VisibleSize.Height) < 1e-9);
                Assert.True(r.VisibleSize.Width > it.VisibleSize.Width);
                Near(Geo.Corners(r)[((int)corner + 2) % 4], opp);
                Assert.Equal(it.Crop, r.Crop);
                Assert.Equal(it.Rotation, r.Rotation);
            }
    }

    [Fact]
    public void ResizeHonoursMinimum()
    {
        var it = Make(200, 100);
        var opp = Geo.Corners(it)[0];
        var r = Geo.Resized(it, Corner.BottomRight, new Point2(opp.X - 300, opp.Y - 300));
        Assert.True(Math.Abs(Math.Max(r.VisibleSize.Width, r.VisibleSize.Height) - 32) < 1e-9);
        Near(Geo.Corners(r)[0], opp);
    }

    [Fact]
    public void RotateFreeAndSnap()
    {
        var it = Make(100, 100, 10, 10);
        static Point2 Pointer(double bearing)
        {
            var t = bearing * Math.PI / 180;
            return new Point2(10 + 50 * Math.Sin(t), 10 - 50 * Math.Cos(t));
        }
        Assert.True(Math.Abs(Geo.Rotated(it, new Point2(100, 10), false).Rotation - 90) < 1e-9);
        Assert.True(Math.Abs(Geo.Rotated(it, Pointer(97), false).Rotation - 97) < 1e-6);
        Assert.True(Math.Abs(Geo.Rotated(it, Pointer(97), true).Rotation - 90) < 1e-9);
        Assert.True(Math.Abs(Geo.Rotated(it, Pointer(98), true).Rotation - 105) < 1e-9);
        var up = Geo.Rotated(it, Pointer(0), false).Rotation;
        Assert.True(up >= 0 && up < 360 && Math.Abs(up) < 1e-6);
        Assert.True(Math.Abs(Geo.Rotated(it, Pointer(270), false).Rotation - 270) < 1e-6);
    }

    [Fact]
    public void FlipKeepsCenter()
    {
        var it = Make(100, 60, 5, 6, 33);
        var f = Geo.Flipped(it);
        Assert.Equal(it.Center, f.Center);
        Assert.True(f.Flipped);
        Assert.False(Geo.Flipped(f).Flipped);
    }

    [Fact]
    public void CropKeepsContentInPlace()
    {
        var region = new NormalizedRect(0.2, 0.1, 0.5, 0.6);
        static Point2 ContentPoint(FreehandItem it)
        {
            var off = new Point2((it.Crop.MidX - 0.5) * it.FullSize.Width, (it.Crop.MidY - 0.5) * it.FullSize.Height);
            if (it.Flipped) off = off with { X = -off.X };
            var r = Rot(off, it.Rotation);
            return new Point2(it.Center.X - r.X, it.Center.Y - r.Y);
        }
        foreach (var rotation in new[] { 0.0, 90, 37 })
            foreach (var flipped in new[] { false, true })
            {
                var it = Make(300, 200, 40, -20, rotation, flipped);
                var c = Geo.Cropped(it, region);
                Assert.Equal(region, c.Crop);
                Near(ContentPoint(c), ContentPoint(it));
                var back = Geo.Cropped(c, CropMath.Unit);
                Near(back.Center, it.Center);
            }
    }

    [Fact]
    public void RotateHandleSitsAboveTopEdge() => Near(Geo.RotateHandle(Make(100, 60, 10, 20, 90), 2), new Point2(54, 20));

    [Fact]
    public void OnScreenFollowsTheCamera()
    {
        var item = Make(100, 100, 500, 350);
        var v = FreehandCamera.Home;
        Assert.True(Geo.IsOnScreen(item, v, Viewport));
        Assert.False(Geo.IsOnScreen(item, v.Panned(new Size2(2000, 0)), Viewport));
        Assert.False(Geo.IsOnScreen(item, v.Panned(new Size2(0, -2000)), Viewport));
        Assert.False(Geo.IsOnScreen(item, v.Zoomed(8, default), Viewport));
    }

    [Fact]
    public void OnScreenEdgesAndViewportCases()
    {
        var item = Make(100, 100, -49, 350);
        Assert.True(Geo.IsOnScreen(item, FreehandCamera.Home, Viewport));
        Assert.False(Geo.IsOnScreen(item with { Center = new Point2(-51, 350) }, FreehandCamera.Home, Viewport));
        Assert.False(Geo.IsOnScreen(item, FreehandCamera.Home, default));
    }

    [Fact]
    public void ScreenBoundsIncludeRotationAndCrop()
    {
        var turned = Make(100, 100, -60, 350, 45);
        Assert.True(Geo.IsOnScreen(turned, FreehandCamera.Home, Viewport));
        Assert.False(Geo.IsOnScreen(turned with { Rotation = 0 }, FreehandCamera.Home, Viewport));
        var cropped = Make(400, 400, -60, 350, crop: new NormalizedRect(0, 0, 0.1, 0.1));
        var bounds = Geo.ScreenBounds(cropped, FreehandCamera.Home);
        Assert.True(Math.Abs(bounds.Width - 40) < 0.001 && Math.Abs(bounds.MidX + 60) < 0.001);
        Assert.False(Geo.IsOnScreen(cropped, FreehandCamera.Home, Viewport));
    }

    [Fact]
    public void SharpDecodeStartsPast1200OnScreen()
    {
        var item = Make(400, 300, 500, 350);
        int Pixels(double zoom) => Geo.DecodePixels(item, new FreehandCamera(new Point2(500 - 500 * zoom, 350 - 350 * zoom), zoom), Viewport);
        Assert.Equal(1600, Pixels(1));
        Assert.Equal(1600, Pixels(3));
        Assert.Equal(3000, Pixels(3.01));
    }

    [Fact]
    public void TightCropIsJudgedByTheFullImage() =>
        Assert.Equal(3000, Geo.DecodePixels(Make(2000, 1000, 500, 350, crop: new NormalizedRect(0.4, 0.4, 0.1, 0.1)), FreehandCamera.Home, Viewport));

    [Fact]
    public void OffScreenImagesKeepTheStandardDecode()
    {
        var item = Make(2000, 1000, 500, 350);
        Assert.Equal(3000, Geo.DecodePixels(item, FreehandCamera.Home, Viewport));
        Assert.Equal(1600, Geo.DecodePixels(item, FreehandCamera.Home.Panned(new Size2(5000, 0)), Viewport));
    }

    [Fact]
    public void ShownCropMirrorsOnlyFlippedItems()
    {
        var rect = new NormalizedRect(0.1, 0.2, 0.3, 0.4);
        var plain = Make(100, 100, crop: rect);
        Assert.Equal(rect, plain.ShownCrop);
        Assert.Equal(rect, plain.StoredCrop(rect));
        var flipped = Make(100, 100, flipped: true, crop: rect);
        Assert.True(Math.Abs(flipped.ShownCrop.X - 0.6) < 1e-9);
        Assert.True(Math.Abs(flipped.StoredCrop(flipped.ShownCrop).X - rect.X) < 1e-9);
    }

    [Fact]
    public void CropFramedOnAFlippedImageKeepsTheKeptPartInPlace()
    {
        var whole = Make(300, 200, 50, 80, 30, true);
        var result = Geo.Cropped(whole, whole.StoredCrop(new NormalizedRect(0.5, 0.25, 0.5, 0.5)));
        Assert.True(Math.Abs(result.Crop.X) < 1e-9);
        var expected = Rot(new Point2(0.25 * 300, 0), 30);
        Near(result.Center, new Point2(whole.Center.X + expected.X, whole.Center.Y + expected.Y));
        Assert.True(Math.Abs(result.VisibleSize.Width - 150) < 1e-9);
    }
}
