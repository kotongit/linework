#if DEBUG
using System.Windows;
using System.Windows.Input;
using Linework.App.Library;
using Linework.App.Platform;
using Linework.App.Views;
using Linework.Core.Freehand;

namespace Linework.App.Debugging;

public static partial class Harness
{
    static async Task ProgressStages(LineworkContext context, LibraryStore library)
    {
        var photo = library.VisiblePhotos[0];
        await Shoot("progress-empty", new BenchmarkProgressView(context, new LibraryStore(Path.Combine(Path.GetDirectoryName(library.Root)!, "shots-empty"), context.App.Host)), 1280, 718);
        library.ToggleBenchmark(photo.Id);
        library.AddAttempt(photo.Id, Guid.NewGuid(), null);
        library.AddAttempt(photo.Id, Guid.NewGuid(), Fixture("sample-person.jpg"));
        await Shoot("progress", new BenchmarkProgressView(context, library), 1280, 718);
        var attempt = library.Attempts(photo.Id).Last();
        await Shoot("comparison", ComparisonWindow.Body(library, attempt, () => { }), 1000, 680);
        await Shoot("attempt", new AttemptSheet(context, library, photo, 3, Guid.NewGuid()).Body(), 520, 460);

        var store = context.App.Freehand!;
        store.LoadIfNeeded();
        await Shoot("freehand-empty", new FreehandView(context, library, store), 1280, 718);
        store.Viewport = new Size2(1280, 718);
        await store.ImportFilesAsync([Fixture("sample-person.jpg"), Fixture("library/Photos/pose one.png")], new Point2(500, 300));
        Check("Freehand import places images", store.Document.Items.Count == 2);
        await Shoot("freehand", new FreehandView(context, library, store), 1280, 718);
        await Settle(0.3);
        var item = store.Document.Items[0];
        store.SelectedId = item.Id;
        store.ChooseTool(FreehandTool.Resize);
        await Shoot("freehand-resize", new FreehandView(context, library, store), 1280, 718);
        await Settle(0.3);
        store.SelectedId = item.Id;
        store.ChooseTool(FreehandTool.Rotate);
        await Shoot("freehand-rotate", new FreehandView(context, library, store), 1280, 718);
        await Settle(0.3);
        store.ChooseTool(FreehandTool.None);
        store.SetGreyscale(true);
        var at = store.Document.Camera.ToScreen(store.Document.Items[^1].Center);
        store.MouseDown(at, 2);
        store.MouseUp();
        await Shoot("freehand-ring", new FreehandView(context, library, store), 1280, 718);
        await Settle(0.3);
        store.Escape();
        store.SetGreyscale(false);
        store.Flush();
        Check("The canvas saved", File.Exists(Path.Combine(store.Canvases.PathOf(store.CurrentName), "canvas.json")));
    }

}
#endif
