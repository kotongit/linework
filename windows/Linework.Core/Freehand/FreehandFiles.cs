using System.Text.Json;
using Linework.Core.Library;

namespace Linework.Core.Freehand;

/// <summary>Reads and writes the Freehand folder: canvas.json, Images/, canvas.damaged.json.</summary>
public sealed class FreehandFiles(string root)
{
    string CanvasPath => Path.Combine(root, "canvas.json");
    string DamagedPath => Path.Combine(root, "canvas.damaged.json");

    public string ImagesDir => Path.Combine(root, "Images");

    sealed record RawDocument(int Version, bool Greyscale, FreehandCamera Camera, IReadOnlyList<FreehandItem> Items);

    /// <summary>Compact, like the Mac app's default encoder. Its decoder doesn't mind the order.</summary>
    public static byte[] Encode(FreehandDocument doc) =>
        JsonSerializer.SerializeToUtf8Bytes(new RawDocument(doc.Version, doc.Greyscale, doc.Camera, doc.Items), LibraryJson.Options);

    /// <summary>Throws JsonException on anything Swift's decoder would refuse.</summary>
    public static FreehandDocument Decode(byte[] data)
    {
        var raw = LibraryJson.Decode<RawDocument>(data);
        return new FreehandDocument(raw.Version, raw.Greyscale, raw.Camera, raw.Items);
    }

    /// <summary>Makes Images/ if needed. Fails if the canvas has gone, rather than recreating it.</summary>
    public void PrepareImagesDir()
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        Directory.CreateDirectory(ImagesDir);
    }

    /// <summary>Where the image is, in whichever Unicode form a Mac stored its name.</summary>
    public string ImagePath(string fileName) => PhotoScan.PathOnDisk(ImagesDir, fileName);

    public bool HasDamagedFile => File.Exists(DamagedPath);

    /// <summary>Missing file: empty. Damaged or newer file: set aside as canvas.damaged.json, empty. A file that's there but can't
    /// be read throws, so a canvas that's only locked or offline isn't replaced by an empty one.
    /// Items whose image is gone are dropped; after a good load, unused images are deleted unless a damaged file exists.</summary>
    public FreehandDocument Load()
    {
        if (!File.Exists(CanvasPath)) return FreehandDocument.Empty;
        byte[] data;
        try
        {
            data = FileRetry.Run(() => File.ReadAllBytes(CanvasPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new UnreadableException("canvas.json", e);
        }
        FreehandDocument doc;
        try
        {
            doc = Decode(data);
            if (doc.Version is < 1 or > FreehandDocument.CurrentVersion) throw new JsonException("Unknown version");
        }
        catch (JsonException)
        {
            SetAsideCanvas();
            return FreehandDocument.Empty;
        }
        var items = doc.Items.Where(i => File.Exists(ImagePath(i.FileName))).ToList();
        if (!HasDamagedFile) RemoveOrphans(items.Select(i => PhotoScan.Comparable(i.FileName)).ToHashSet());
        return doc with { Camera = doc.Camera.Sanitized, Items = items };
    }

    /// <summary>Atomic write of canvas.json.</summary>
    public void Save(FreehandDocument doc)
    {
        // No intermediates: if the library has gone, this fails rather than making a new one.
        if (!Directory.Exists(root))
        {
            if (!Directory.Exists(Path.GetDirectoryName(root))) throw new DirectoryNotFoundException(root);
            Directory.CreateDirectory(root);
        }
        AtomicFile.Write(CanvasPath, Encode(doc));
    }

    public void RemoveImage(string fileName)
    {
        try { File.Delete(ImagePath(fileName)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    void SetAsideCanvas()
    {
        try
        {
            FileRetry.Run(() => File.Move(CanvasPath, LibraryFiles.FreeAsideName(CanvasPath)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Could not keep the bad file: leave it where it is rather than lose it.
        }
    }

    void RemoveOrphans(IReadOnlySet<string> used)
    {
        if (!Directory.Exists(ImagesDir)) return;
        foreach (var path in Directory.EnumerateFileSystemEntries(ImagesDir))
            if (!used.Contains(PhotoScan.Comparable(Path.GetFileName(path)))) RemoveImage(Path.GetFileName(path));
    }
}
