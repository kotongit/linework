using Linework.Core.Crop;
using R = Linework.Core.Crop.NormalizedRect;

namespace Linework.Core.Tests;

static class Approx
{
    public static void Rect(R a, R b, double tol = 1e-9)
    {
        Assert.True(Math.Abs(a.X - b.X) < tol && Math.Abs(a.Y - b.Y) < tol && Math.Abs(a.Width - b.Width) < tol && Math.Abs(a.Height - b.Height) < tol, $"{a} vs {b}");
    }
}

public class CropMathTests
{
    [Fact]
    public void FromVisionFlipsY() => Approx.Rect(CropMath.FromVision(new R(0.1, 0.2, 0.3, 0.4)), new R(0.1, 0.4, 0.3, 0.4));

    [Fact]
    public void UnionOfTwo()
    {
        Approx.Rect(CropMath.Union([new R(0, 0, 0.2, 0.2), new R(0.5, 0.5, 0.1, 0.1)])!.Value, new R(0, 0, 0.6, 0.6));
        Assert.Null(CropMath.Union([]));
    }

    [Fact]
    public void PaddedGrowsAndClamps()
    {
        Approx.Rect(CropMath.Padded(new R(0.4, 0.4, 0.2, 0.2)), new R(0.384, 0.384, 0.232, 0.232));
        Approx.Rect(CropMath.Padded(CropMath.Unit), CropMath.Unit);
    }

    [Fact]
    public void AutoCropNullForNoBoxes() => Assert.Null(CropMath.AutoCrop([]));

    [Fact]
    public void FitAspect() => Approx.Rect(CropMath.FitAspect(CropMath.Unit, 2.0 / 3.0, 1.0), new R(1.0 / 6.0, 0, 2.0 / 3.0, 1));

    [Fact]
    public void PixelRect() => Assert.Equal(new R(100, 100, 200, 50), CropMath.PixelRect(new R(0.25, 0.5, 0.5, 0.25), 400, 200));

    [Fact]
    public void BoundsOfSetPixels() =>
        Assert.Equal(new R(0.2, 0.2, 0.3, 0.6), CropMath.MaskBounds(10, 5, (x, y) => x is >= 2 and <= 4 && y is >= 1 and <= 3));

    [Fact]
    public void EmptyMaskIsNull() => Assert.Null(CropMath.MaskBounds(4, 4, (_, _) => false));
}

public class CropEditingTests
{
    static readonly R Box = new(0.2, 0.2, 0.4, 0.4);

    [Fact]
    public void MoveStaysInside() => Approx.Rect(CropMath.Moved(new R(0.5, 0.5, 0.4, 0.4), 0.3, -0.7), new R(0.6, 0, 0.4, 0.4));

    [Fact]
    public void ResizeCornerFree() => Approx.Rect(CropMath.Resized(Box, CropHandle.BottomRight, 0.1, 0.2, null, 1), new R(0.2, 0.2, 0.5, 0.6));

    [Fact]
    public void ResizeKeepsMinimumSize() => Approx.Rect(CropMath.Resized(Box, CropHandle.TopLeft, 0.9, 0.9, null, 1), new R(0.55, 0.55, 0.05, 0.05));

    [Fact]
    public void ResizeClampsToUnit() => Approx.Rect(CropMath.Resized(Box, CropHandle.Right, 1, 0, null, 1), new R(0.2, 0.2, 0.8, 0.4));

    [Fact]
    public void ResizeAspectLocked()
    {
        Approx.Rect(CropMath.Resized(new R(0, 0, 0.5, 0.5), CropHandle.BottomRight, 0.1, 0, 1, 1), new R(0, 0, 0.6, 0.6));
        var t = CropMath.Resized(new R(0, 0, 0.4, 0.6), CropHandle.BottomRight, 0.2, 0, 2.0 / 3.0, 1);
        Assert.True(Math.Abs(t.Width / t.Height - 2.0 / 3.0) < 1e-9);
        Assert.True(t.MaxY <= 1 + 1e-9);
    }

    [Fact]
    public void MirroredX() => Approx.Rect(CropMath.MirroredX(new R(0.1, 0.2, 0.3, 0.4)), new R(0.6, 0.2, 0.3, 0.4));
}
