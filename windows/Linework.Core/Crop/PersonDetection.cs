namespace Linework.Core.Crop;

/// <summary>Reads what the person-detection model says. The model itself runs in the app; this is plain arithmetic, so it's tested everywhere.</summary>
public static class PersonDetection
{
    static readonly int[] Strides = [8, 16, 32];
    const int Fields = 85; // cx, cy, w, h, object, then 80 classes with person first
    const float ScoreThreshold = 0.4f;

    /// <summary>The image is scaled to fit the model's square input, keeping its shape, and sits in the top-left corner.</summary>
    public static double Scale(int imageWidth, int imageHeight, int inputSize) =>
        Math.Min((double)inputSize / imageWidth, (double)inputSize / imageHeight);

    /// <summary>Normalized top-left boxes of the people found in the model's raw output.</summary>
    public static IReadOnlyList<NormalizedRect> Boxes(ReadOnlySpan<float> output, int inputSize, int imageWidth, int imageHeight)
    {
        var boxes = new List<NormalizedRect>();
        var cells = Strides.Sum(s => inputSize / s * (inputSize / s));
        if (output.Length != cells * Fields) return boxes;
        var scale = Scale(imageWidth, imageHeight, inputSize);
        var position = 0;
        foreach (var stride in Strides)
        {
            var grid = inputSize / stride;
            for (var gy = 0; gy < grid; gy++)
                for (var gx = 0; gx < grid; gx++, position += Fields)
                {
                    if (output[position + 4] * output[position + 5] < ScoreThreshold) continue;
                    double cx = (output[position] + gx) * stride, cy = (output[position + 1] + gy) * stride;
                    double w = Math.Exp(output[position + 2]) * stride, h = Math.Exp(output[position + 3]) * stride;
                    boxes.Add(new NormalizedRect((cx - w / 2) / scale / imageWidth, (cy - h / 2) / scale / imageHeight, w / scale / imageWidth, h / scale / imageHeight));
                }
        }
        return boxes;
    }

    /// <summary>The padded union of the boxes, or the full frame with found false.</summary>
    public static (NormalizedRect Rect, bool Found) AutoCrop(IReadOnlyList<NormalizedRect> boxes) =>
        CropMath.AutoCrop(boxes) is { } rect ? (rect, true) : (CropMath.Unit, false);
}

public static class ExifOrientation
{
    /// <summary>The clockwise turn and then horizontal mirror that show a file the right way up.</summary>
    public static (int Rotation, bool Flipped) Transform(int orientation) => orientation switch
    {
        2 => (0, true),
        3 => (180, false),
        4 => (180, true),
        5 => (90, true),
        6 => (90, false),
        7 => (270, true),
        8 => (270, false),
        _ => (0, false),
    };
}
