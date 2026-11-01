using Linework.Core.Crop;

namespace Linework.Core.Tests;

public class DetectionTests
{
    const int Size = 416, Cells = 3549, Fields = 85;

    /// <summary>One detection at stride 8, in the model's raw form: offsets in the cell, log sizes, object and class scores.</summary>
    static float[] Output(params (int Cell, float Width, float Height, float Object, float Person)[] hits)
    {
        var output = new float[Cells * Fields];
        foreach (var (cell, w, h, obj, person) in hits)
        {
            var at = cell * Fields;
            output[at] = 0.5f;
            output[at + 1] = 0.5f;
            output[at + 2] = (float)Math.Log(w / 8);
            output[at + 3] = (float)Math.Log(h / 8);
            output[at + 4] = obj;
            output[at + 5] = person;
        }
        return output;
    }

    [Fact]
    public void DecodesAPersonBox()
    {
        // Cell 52 * 26 + 26 is the middle of the 8-stride grid: (212, 212) in the 416 input.
        var cell = 52 * 26 + 26;
        var boxes = PersonDetection.Boxes(Output((cell, 104, 208, 0.9f, 0.9f)), Size, 416, 416);
        var box = Assert.Single(boxes);
        Approx.Rect(box, new NormalizedRect(160.0 / 416, 108.0 / 416, 0.25, 0.5), 1e-5);
    }

    [Fact]
    public void ScalesBackFromTheLetterbox()
    {
        // An 832×416 image is scaled by a half, so it fills the top half of the 416 input.
        var cell = 52 * 13 + 26;
        var box = Assert.Single(PersonDetection.Boxes(Output((cell, 104, 104, 0.9f, 0.9f)), Size, 832, 416));
        Approx.Rect(box, new NormalizedRect(160.0 / 416, 112.0 / 416, 0.25, 0.5), 1e-5);
    }

    [Fact]
    public void LowScoresAreIgnored() => Assert.Empty(PersonDetection.Boxes(Output((5, 50, 50, 0.5f, 0.5f)), Size, 416, 416));

    [Fact]
    public void AWrongSizedOutputFindsNothing() => Assert.Empty(PersonDetection.Boxes(new float[10], Size, 416, 416));

    [Fact]
    public void LetterboxScaleFitsTheLongEdge()
    {
        Assert.Equal(0.5, PersonDetection.Scale(832, 416, Size));
        Assert.Equal(2, PersonDetection.Scale(100, 208, Size));
    }

    [Fact]
    public void UnionOfPersonBoxesIsPadded()
    {
        var (rect, found) = PersonDetection.AutoCrop([new NormalizedRect(0.2, 0.2, 0.1, 0.1), new NormalizedRect(0.4, 0.4, 0.1, 0.1)]);
        Assert.True(found);
        Approx.Rect(rect, CropMath.Padded(new NormalizedRect(0.2, 0.2, 0.3, 0.3)));
    }

    [Fact]
    public void NoPersonFallsBack()
    {
        var (rect, found) = PersonDetection.AutoCrop([]);
        Assert.False(found);
        Assert.Equal(CropMath.Unit, rect);
    }

    [Theory]
    [InlineData(1, 0, false), InlineData(2, 0, true), InlineData(3, 180, false), InlineData(4, 180, true), InlineData(5, 90, true), InlineData(6, 90, false), InlineData(7, 270, true), InlineData(8, 270, false), InlineData(0, 0, false)]
    public void ExifOrientationBecomesATurnAndMirror(int orientation, int rotation, bool flipped) =>
        Assert.Equal((rotation, flipped), ExifOrientation.Transform(orientation));
}
