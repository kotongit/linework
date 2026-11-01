using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Linework.Core.Crop;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Linework.App.Library;

/// <summary>Finds the figure(s) with a small person-detection model on the CPU, falling back to the full frame.</summary>
public static class AutoCropper
{
    const int InputSize = 416;
    static readonly Lazy<InferenceSession?> Session = new(MakeSession);

    static InferenceSession? MakeSession()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Models", "yolox_nano.onnx");
            return new InferenceSession(path, new SessionOptions { IntraOpNumThreads = 2 });
        }
        catch (Exception e) when (e is OnnxRuntimeException or DllNotFoundException or TypeInitializationException or IOException or BadImageFormatException)
        {
            // The app works without it: imports just arrive as the full frame.
            return null;
        }
    }

    public static bool ModelLoads => Session.Value != null;

    public static (NormalizedRect Rect, bool Found) Crop(string path)
    {
        var image = ImageRendering.Load(path, InputSize);
        return image == null ? (CropMath.Unit, false) : Crop(image);
    }

    /// <summary>The image must be already oriented.</summary>
    public static (NormalizedRect Rect, bool Found) Crop(BitmapSource image)
    {
        if (Session.Value is not { } session) return (CropMath.Unit, false);
        int w = image.PixelWidth, h = image.PixelHeight;
        var scale = Linework.Core.Crop.PersonDetection.Scale(w, h, InputSize);
        var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        var bgr = new FormatConvertedBitmap(scaled, PixelFormats.Bgr24, null, 0);
        int sw = bgr.PixelWidth, sh = bgr.PixelHeight, stride = sw * 3;
        var pixels = new byte[stride * sh];
        bgr.CopyPixels(pixels, stride, 0);

        // Blue, green, red planes from 0 to 255, on a grey ground.
        var input = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        for (var c = 0; c < 3; c++)
            for (var y = 0; y < InputSize; y++)
                for (var x = 0; x < InputSize; x++)
                    input[0, c, y, x] = y < sh && x < sw ? pixels[y * stride + x * 3 + c] : 114f;
        try
        {
            using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
            var output = results[0].AsTensor<float>().ToArray();
            return Linework.Core.Crop.PersonDetection.AutoCrop(Linework.Core.Crop.PersonDetection.Boxes(output, InputSize, w, h));
        }
        catch (OnnxRuntimeException)
        {
            return (CropMath.Unit, false);
        }
    }
}
