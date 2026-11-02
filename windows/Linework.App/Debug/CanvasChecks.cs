#if DEBUG
using Linework.App.Library;
using Linework.Core.Crop;
using Linework.Core.Freehand;
using Linework.Core.Library;

namespace Linework.App.Debugging;

/// <summary>The canvas and tool checks, against the real store. Shared by dotnet test on the Mac and the harness on Windows.</summary>
public static class CanvasChecks
{
    static bool Near(Point2 a, Point2 b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;

    public static async Task RunAsync(string path, ILibraryHost host, string source, Action<string, bool> check)
    {
        Directory.CreateDirectory(path);
        (FreehandStore Store, CanvasDirectory Directory, string Root) Fresh(string name)
        {
            var root = Path.Combine(path, $"{name}-{Guid.NewGuid()}");
            Directory.CreateDirectory(root);
            LibraryFolder.Create(root, "2026-11-02", host.Now);
            var directory = new CanvasDirectory(Path.Combine(root, "Canvases"));
            return (new FreehandStore(directory, Guid.NewGuid(), host, new Settings(null)), directory, root);
        }

        // Canvases.
        var (canvas, dir, _) = Fresh("canvases");
        canvas.LoadIfNeeded();
        check("A library starts with a canvas", dir.List().Count == 1 && canvas.CurrentName.Length > 0);
        var first = canvas.CurrentName;
        canvas.SetGreyscale(true);
        canvas.NewCanvas();
        check("New makes and opens another", canvas.CurrentName != first && dir.List().Count == 2 && !canvas.Document.Greyscale);
        canvas.Open(first);
        check("Switching back keeps its state", canvas.Document.Greyscale);
        canvas.RenameCurrent("Figure refs");
        check("Rename moves the folder", dir.List().Contains("Figure refs") && canvas.CurrentName == "Figure refs");
        check("A renamed canvas keeps its state", canvas.Document.Greyscale);
        canvas.DuplicateCurrent();
        check("Duplicate opens the copy", canvas.CurrentName == "Figure refs copy" && canvas.Document.Greyscale);
        Directory.CreateDirectory(dir.PathOf("From File Explorer"));
        canvas.RefreshList();
        check("A folder added in File Explorer appears", canvas.Names.Contains("From File Explorer"));
        foreach (var _ in dir.List()) canvas.DeleteCurrent();
        check("Deleting them all leaves one canvas", dir.List().Count == 1);

        // A name pasted in decomposed Unicode is stored composed, and the store must follow it.
        var (n, ndir, _) = Fresh("nfd-rename");
        n.LoadIfNeeded();
        n.SetGreyscale(true);
        n.RenameCurrent("cafe\u0301");
        check("A rename follows the name the folder got", n.CurrentName == "caf\u00E9" && ndir.List().Count == 1);
        n.SetGreyscale(false);
        n.Flush();
        check("Saving after it doesn't split the canvas", Directory.GetDirectories(ndir.CanvasesRoot).Length == 1);

        // The library goes away with a canvas open.
        var (g, _, groot) = Fresh("gone");
        g.LoadIfNeeded();
        g.Flush();
        Directory.Move(groot, groot + ".away");
        g.SetGreyscale(!g.Document.Greyscale);
        check("A failed canvas save says so", !g.Flush() && g.Status != null);
        check("The library isn't recreated by a canvas", !Directory.Exists(groot));
        var goneName = g.CurrentName;
        g.Open("Elsewhere");
        check("A failed save stops a canvas switch", g.CurrentName == goneName);
        Directory.Move(groot + ".away", groot);
        check("The save succeeds once it's back", g.Flush());

        // An import that outlives a canvas switch.
        var (h, hdir, _) = Fresh("import-switch");
        h.LoadIfNeeded();
        var startedIn = h.CurrentName;
        var importing = h.ImportFilesAsync([source, source], default);
        h.NewCanvas();
        await importing;
        check("An import doesn't land on another canvas", h.Document.Items.Count == 0);
        var left = Directory.Exists(Path.Combine(hdir.PathOf(startedIn), "Images")) ? Directory.GetFiles(Path.Combine(hdir.PathOf(startedIn), "Images")).Length : 0;
        check("An abandoned import leaves no copies", left == 0);

        // Importing, and the tools, through the store's pointer calls.
        var (s, _, _) = Fresh("tools");
        var size = new Size2(1000, 700);
        s.Viewport = size;
        s.LoadIfNeeded();
        var notImage = Path.Combine(path, $"not-{Guid.NewGuid()}.txt");
        File.WriteAllText(notImage, "x");
        await s.ImportFilesAsync([source, notImage], new Point2(500, 350));
        check("Import places the image and skips the rest", s.Document.Items.Count == 1 && s.Status == "Skipped 1 file");
        FreehandItem Item() => s.Document.Items[0];
        check("It's selected, 400 points on its long edge", s.SelectedId == Item().Id && Math.Abs(Math.Max(Item().FullSize.Width, Item().FullSize.Height) - 400) < 1e-9);
        var id = Item().Id;
        void Click(Point2 p, int clicks = 1)
        {
            s.MouseDown(p, clicks);
            s.MouseUp();
        }
        void DragTo(Point2 a, Point2 b, bool shift = false)
        {
            s.MouseDown(a, 1);
            s.MouseDragged(b, shift);
            s.MouseUp();
        }
        var middle = new Point2(500, 350);
        Click(middle, 2);
        check("Double click opens the ring", s.RingAt == middle);
        var offsets = FreehandRing.Offsets();
        check("Five buttons on a 64pt radius, first straight up", offsets.Count == 5 && Near(offsets[0], new Point2(0, -64)));
        foreach (var corner in new[] { new Point2(0, 0), new Point2(1000, 0), new Point2(0, 700), new Point2(1000, 700) })
        {
            var c = FreehandRing.Center(corner, size, 300);
            check($"A ring near {corner.X},{corner.Y} stays inside", offsets.All(o => c.X + o.X - 21 >= -1e-9 && c.X + o.X + 21 <= 1000 + 1e-9 && c.Y + o.Y - 21 >= -1e-9 && c.Y + o.Y + 21 <= 700 + 1e-9));
        }
        check("A ring in the open isn't moved", FreehandRing.Center(middle, size, 300) == middle);
        var over = FreehandRing.Center(new Point2(500, 700), size, 300);
        check("Over the bar, the ring stops above it", over.X == 500 && Math.Abs(over.Y + offsets.Max(o => o.Y) + 21 - (700 - 66)) < 1e-9);
        var beside = FreehandRing.Center(new Point2(100, 700), size, 300);
        check("Beside the bar, the ring can use the bottom", Math.Abs(beside.Y + offsets.Max(o => o.Y) + 21 - 700) < 1e-9);

        var before = Item();
        s.FlipSelected();
        check("Flip mirrors at once, in place, and closes the ring", Item().Flipped != before.Flipped && Item().Center == before.Center && s.RingAt == null);
        s.ChooseTool(FreehandTool.Resize);
        check("Resize enters resize mode", s.Tool == FreehandTool.Resize);
        var start = Item();
        var corners = FreehandGeometry.Corners(start);
        var grabbed = s.Document.Camera.ToScreen(corners[2]);
        DragTo(grabbed, new Point2(grabbed.X + 100, grabbed.Y + 100));
        var grown = Item();
        check("Dragging a corner scales it around the opposite corner", Near(FreehandGeometry.Corners(grown)[0], corners[0]) && grown.FullSize.Width > start.FullSize.Width);
        check("Proportions kept", Math.Abs(grown.FullSize.Width / grown.FullSize.Height - start.FullSize.Width / start.FullSize.Height) < 1e-9);
        var c0 = FreehandGeometry.Corners(grown)[2];
        var body = Item().Center;
        DragTo(s.Document.Camera.ToScreen(body), new Point2(s.Document.Camera.ToScreen(body).X + 10, s.Document.Camera.ToScreen(body).Y));
        check("Dragging the body moves it in a mode", s.Tool == FreehandTool.Resize && Math.Abs(Item().Center.X - body.X - 10) < 1e-9);
        s.Escape();
        check("Esc ends the mode", s.Tool == FreehandTool.None && s.SelectedId != null);

        s.ChooseTool(FreehandTool.Rotate);
        var handle = s.Document.Camera.ToScreen(FreehandGeometry.RotateHandle(Item(), s.Document.Camera.Zoom));
        var center = s.Document.Camera.ToScreen(Item().Center);
        DragTo(handle, new Point2(center.X + 100, center.Y));
        check("Dragging the handle turns it freely", Math.Abs(Item().Rotation - 90) < 1e-6);
        var handle2 = s.Document.Camera.ToScreen(FreehandGeometry.RotateHandle(Item(), s.Document.Camera.Zoom));
        var angle = 32 * Math.PI / 180;
        center = s.Document.Camera.ToScreen(Item().Center);
        DragTo(handle2, new Point2(center.X + 100 * Math.Sin(angle), center.Y - 100 * Math.Cos(angle)), true);
        check("Shift snaps to 15 degrees", Math.Abs(Item().Rotation - 30) < 1e-6);
        Click(new Point2(5, 5));
        check("A click on empty space ends the mode", s.Tool == FreehandTool.None && s.SelectedId == null);

        // Crop.
        s.SelectedId = id;
        var whole = Item();
        s.BeginCrop();
        check("Crop opens the crop window", s.CropId == id);
        var shown = new NormalizedRect(0.5, 0, 0.5, 1);
        s.ApplyCrop(whole.StoredCrop(shown));
        s.EndCrop();
        check("The crop is stored in the file's own space", Item().Flipped ? Math.Abs(Item().Crop.X) < 1e-9 : Math.Abs(Item().Crop.X - 0.5) < 1e-9);
        check("Closing ends the crop", s.CropId == null);

        // The ring closes when the view moves, and a click's wobble doesn't.
        s.MouseDown(s.Document.Camera.ToScreen(Item().Center), 2);
        s.MouseUp();
        check("The second press opens the ring", s.RingAt != null);
        s.Scroll(new Size2(5, 5));
        check("Scrolling closes the ring", s.RingAt == null);
        var press = s.Document.Camera.ToScreen(Item().Center);
        s.MouseDown(press, 2);
        s.MouseDragged(new Point2(press.X + 1, press.Y), false);
        check("A wobble on the second press leaves the ring", s.RingAt == press);
        s.MouseDragged(new Point2(press.X + 10, press.Y), false);
        check("Dragging past the threshold closes the ring", s.RingAt == null);
        s.MouseUp();
        s.MouseDown(s.Document.Camera.ToScreen(Item().Center), 2);
        s.MouseUp();
        s.ChooseTool(FreehandTool.Resize);
        s.Leave();
        check("Leaving clears the ring and the tool", s.RingAt == null && s.Tool == FreehandTool.None);

        // Fit and Clear.
        s.Fit();
        check("Fit stays at or under 100%", s.Document.Camera.Zoom <= 1);
        s.SetGreyscale(true);
        var imageFile = s.Files.ImagePath(Item().FileName);
        check("The copy is on disk", File.Exists(imageFile));
        var keptCamera = s.Document.Camera;
        s.Clear();
        check("Clear removes every image and its file", s.Document.Items.Count == 0 && !File.Exists(imageFile));
        check("Clear keeps greyscale, zoom and position", s.Document.Greyscale && s.Document.Camera == keptCamera);
        s.Fit();
        check("Fit with no images returns home", s.Document.Camera == FreehandCamera.Home);

        // A canvas that can't be read isn't replaced by an empty one.
        var (u, _, _) = Fresh("unreadable");
        u.LoadIfNeeded();
        u.SetGreyscale(true);
        u.Flush();
        var canvasPath = Path.Combine(u.Canvases.PathOf(u.CurrentName), "canvas.json");
        var bytes = File.ReadAllBytes(canvasPath);
        var name = u.CurrentName;
        u.NewCanvas();
        using (StoreChecks.Lock(canvasPath))
        {
            u.Open(name);
            check("An unreadable canvas says so", u.Status == "Couldn't read this canvas");
            u.SetGreyscale(true);
            check("It isn't saved over", !u.Flush());
        }
        check("The canvas file is untouched", File.ReadAllBytes(canvasPath).SequenceEqual(bytes));
        u.RefreshList();
        check("It loads once it can be read", u.Status == null && u.Document.Greyscale);

        // Notes over the canvas.
        var notes = new FreehandNotes("Ana", new Random(1));
        check("No note in the first five minutes", !notes.Update(0, false, true, null) && !notes.Update(299, false, true, null));
        check("A due note waits while the circular menu is open", !notes.Update(301, true, true, null) && notes.Shown == null);
        check("The note appears once the menu closes", notes.Update(302, false, true, null) && notes.Shown != null);
        notes.Update(303, false, false, null);
        check("Turning the setting off hides the note at once", notes.Shown == null);
        notes.Update(310, false, true, null);
        check("Turning it on starts a new visit", !notes.Update(600, false, true, null) && notes.Update(611, false, true, null));
        notes.Sleep();
        check("Sleep hides the note", notes.Shown == null);
        notes.Update(700, false, true, null);
        check("Wake restarts the visit clock", notes.Update(1001, false, true, null) && notes.ChosenAtVisitSeconds < 302);
    }
}
#endif
