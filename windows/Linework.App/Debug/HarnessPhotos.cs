#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Linework.App.Library;
using Linework.App.Platform;
using Linework.App.Views;
using Linework.Core.Crop;

namespace Linework.App.Debugging;

public static partial class Harness
{
    /// <summary>The fixtures folder in the repo the harness was built from.</summary>
    static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "shared", "fixtures"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "shared", "fixtures", name);
    }

    static async Task PhotoStages(string path, LineworkContext context, LibraryStore library)
    {
        var sample = Fixture("sample-person.jpg");
        var host = new WpfHost();
        Check("The model loads", AutoCropper.ModelLoads);
        var (rect, found) = AutoCropper.Crop(sample);
        Check("ModelFindsAPersonInTheSample", found && rect.Width < 1 && rect.Width > 0.5 && rect.Height > 0.8);

        await PhotoChecks.RunAsync(Path.Combine(path, "photo-checks"), host, sample, Fixture("library/Photos/pose one.png"), Check);

        // EXIF: a sideways file shows upright.
        var turned = Path.Combine(path, "exif-6.jpg");
        var source = new BitmapImage(new Uri(sample));
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
        var encoder = new JpegBitmapEncoder();
        var wide = new CroppedBitmap(source, new Int32Rect(0, 0, 400, 200));
        encoder.Frames.Add(BitmapFrame.Create(wide, null, metadata, null));
        await using (var file = File.Create(turned)) encoder.Save(file);
        var upright = ImageRendering.Load(turned, 400);
        Check("EXIF orientation is applied", upright is { PixelWidth: 200, PixelHeight: 400 });

        // The render cache stays within its limit.
        var cached = await RenderCache.Shared.RenderAsync(new RenderRequest(sample, 0, false, CropMath.Unit, 512));
        Check("A render is cached", cached != null && RenderCache.Shared.Get(new RenderRequest(sample, 0, false, CropMath.Unit, 512)) != null);
        Check("The cache respects its cap", RenderCache.Shared.TotalCost <= 256L * 1024 * 1024);

        // Shots.
        var result = await ImportService.ImportAsync([sample, Fixture("library/Photos/pose one.png")], library, host, null);
        Check("Shots library has photos", result.Imported == 2);
        var set = library.CreateSet("Figures");
        library.SetPhoto(library.Photos[0].Id, set.Id, true);
        library.Flush();
        var view = new LibraryView(context, library);
        await Shoot("library", view, 1280, 718);
        await Shoot("library-empty", new LibraryView(context, new LibraryStore(EmptyLibrary(path), host)), 1280, 718);
        var preview = new ImagePreviewWindow(context, library, library.Photos.Select(p => p.Id).ToList(), 0, _ => { });
        var previewBody = preview.Body();
        preview.Content = null;
        await Shoot("preview", previewBody, 1000, 760);
        preview.Close();
        var crop = CropEditorWindow.ForShot(context, sample);
        crop.Left = -20000;
        crop.ShowActivated = false;
        crop.Show();
        await Settle(1);
        var cropBody = crop.Content as FrameworkElement;
        Check("The crop editor shows the image", cropBody != null && (Descendants(cropBody).OfType<Image>().Any()));
        crop.Content = null;
        await Shoot("crop", cropBody!, 900, 660);
        crop.Close();
    }

    static string EmptyLibrary(string path)
    {
        var root = Path.Combine(path, "shots-empty");
        Directory.CreateDirectory(root);
        Linework.Core.Library.LibraryFolder.Create(root, "2026-11-02", DateTimeOffset.Now);
        return root;
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}
#endif
