#if DEBUG
using Linework.App.Library;
using Linework.Core.Crop;
using Linework.Core.Library;

namespace Linework.App.Debugging;

/// <summary>The photo checks, shared by dotnet test on the Mac and the harness on Windows.</summary>
public static class PhotoChecks
{
    public static async Task RunAsync(string path, ILibraryHost host, string sourceA, string sourceB, Action<string, bool> check)
    {
        LibraryStore Fresh(string name)
        {
            var root = Path.Combine(path, $"{name}-{Guid.NewGuid()}");
            Directory.CreateDirectory(root);
            LibraryFolder.Create(root, "2026-11-02", host.Now);
            return new LibraryStore(root, host);
        }
        Directory.CreateDirectory(path);
        var store = Fresh("photos");
        var sources = new[] { sourceA, sourceB };
        var notImage = Path.Combine(path, $"not-an-image-{Guid.NewGuid()}.txt");
        File.WriteAllText(notImage, "hello");
        var result = await ImportService.ImportAsync([.. sources, notImage], store, host, null);
        File.Delete(notImage);
        check("Import takes photos and skips the rest", result.Imported == 2 && result.Skipped == 1);
        check("Imports keep readable names", store.Photos.All(p => sources.Select(Path.GetFileName).Contains(p.FileName)));
        var again = await ImportService.ImportAsync([sourceA], store, host, null);
        check("A second copy gets its own name", again.Imported == 1 && store.Photos.Any(p => p.FileName.Contains(" 2.", StringComparison.Ordinal)));

        var photo = store.VisiblePhotos.First(p => !p.FileName.Contains(" 2.", StringComparison.Ordinal));
        var crop = new NormalizedRect(0.2, 0.2, 0.4, 0.4);
        store.Update(photo.Id, p => p with { Crop = crop });
        var url = store.PhotoPath(photo);
        var aside = Path.Combine(path, $"aside-{Guid.NewGuid()}.img");

        File.Move(url, aside);
        await PhotoScanner.RunAsync(store, host);
        check("A photo deleted in File Explorer is hidden, not forgotten", store.Photo(photo.Id)?.Missing == true && store.VisiblePhotos.All(p => p.Id != photo.Id));
        File.Move(aside, url);
        await PhotoScanner.RunAsync(store, host);
        check("Restoring the file brings it back with its crop", store.Photo(photo.Id)?.Missing == false && store.Photo(photo.Id)?.Crop == crop);

        var renamed = Path.Combine(store.PhotosDir, $"renamed-{Guid.NewGuid()}{Path.GetExtension(url)}");
        File.Move(url, renamed);
        await PhotoScanner.RunAsync(store, host);
        check("A rename in File Explorer keeps its crop", store.Photo(photo.Id)?.FileName == Path.GetFileName(renamed) && store.Photo(photo.Id)?.Crop == crop);

        var count = store.VisiblePhotos.Count;
        var extra = Path.Combine(store.PhotosDir, "dropped-in" + Path.GetExtension(sourceB));
        File.Copy(sourceB, extra);
        await PhotoScanner.RunAsync(store, host);
        check("A file added in File Explorer appears", store.VisiblePhotos.Count == count + 1 && store.Photos.Any(p => p.FileName == Path.GetFileName(extra)));

        var holding = Path.Combine(store.Root, "Photos-away");
        Directory.Move(store.PhotosDir, holding);
        await PhotoScanner.RunAsync(store, host);
        check("A missing Photos folder changes nothing", store.VisiblePhotos.Count == count + 1 && store.Notice != null);
        check("Photos stays gone rather than remade", !Directory.Exists(store.PhotosDir));
        Directory.Move(holding, store.PhotosDir);
        store.Notice = null;

        var victim = store.Photos.First(p => p.FileName == Path.GetFileName(extra));
        var victimPath = store.PhotoPath(victim);
        store.DeletePhoto(victim.Id);
        check("Deleting removes the record and the file", store.Photo(victim.Id) == null && !File.Exists(victimPath));
        store.Flush();
        check("Photo records survive reopening", new LibraryStore(store.Root, host).Photos.Count == store.Photos.Count);

        // Lost photo records must not make every file a new photo.
        var b = Fresh("lost-records");
        await ImportService.ImportAsync(sources, b, host, null);
        var keep = b.VisiblePhotos[0];
        b.ToggleBenchmark(keep.Id);
        var set = b.CreateSet("Set");
        b.SetPhoto(keep.Id, set.Id, true);
        b.Flush();
        File.WriteAllText(b.Files.PathOf(LibraryFile.Photos), "junk");
        var damaged = new LibraryStore(b.Root, host);
        await PhotoScanner.RunAsync(damaged, host);
        check("Lost photo records don't turn files into new photos", damaged.Photos.Count == 0 && damaged.Notice != null);
        damaged.Flush();
        var reopened = new LibraryStore(b.Root, host);
        check("The set and benchmark still point at the old photo", reopened.Sets[0].PhotoIDs.SequenceEqual([keep.Id]) && reopened.IsBenchmark(keep.Id));

        // A name held by a missing record isn't free.
        var c = Fresh("shared-name");
        await ImportService.ImportAsync([sourceA], c, host, null);
        var first = c.VisiblePhotos[0];
        File.Move(c.PhotoPath(first), Path.Combine(path, $"aside-{Guid.NewGuid()}"));
        await PhotoScanner.RunAsync(c, host);
        await ImportService.ImportAsync([sourceA], c, host, null);
        check("Importing can't take a missing photo's name", c.Photos.Select(p => p.FileName).Distinct().Count() == 2);

        // A photo stored in decomposed Unicode, from a Mac.
        var d = Fresh("nfd");
        File.Copy(sourceA, Path.Combine(d.PhotosDir, "José.jpg"));
        await PhotoScanner.RunAsync(d, host);
        var adopted = d.Photos.Single();
        d.Update(adopted.Id, p => p with { FileName = "José.jpg" });
        await PhotoScanner.RunAsync(d, host);
        check("A decomposed name on disk matches its record", d.VisiblePhotos.Count == 1 && d.Photos.Count == 1 && File.Exists(d.PhotoPath(d.Photos[0])));
    }
}
#endif
