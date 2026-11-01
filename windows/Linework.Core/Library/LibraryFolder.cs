using Linework.Core.Plan;

namespace Linework.Core.Library;

public enum FolderKind { Library, Empty, OtherContents, Unavailable, NewerFormat }

/// <summary>What a chosen folder is, and how to make it a library.</summary>
public static class LibraryFolder
{
    public static readonly IReadOnlyList<string> Folders = ["Photos", "Drawings", "Canvases", "Progress"];
    public const string ManifestName = "library.json";

    public static FolderKind Classify(string path)
    {
        if (!Directory.Exists(path)) return FolderKind.Unavailable;
        List<string> contents;
        try
        {
            contents = Directory.EnumerateFileSystemEntries(path).Select(p => Path.GetFileName(p)!).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return FolderKind.Unavailable;
        }
        byte[]? data = null;
        try
        {
            data = File.ReadAllBytes(Path.Combine(path, ManifestName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (data != null)
        {
            try
            {
                return LibraryJson.Decode<LibraryManifest>(data).Format > LibraryManifest.CurrentFormat ? FolderKind.NewerFormat : FolderKind.Library;
            }
            catch (System.Text.Json.JsonException)
            {
                return FolderKind.OtherContents;
            }
        }
        return contents.All(IsSystemFile) ? FolderKind.Empty : FolderKind.OtherContents;
    }

    /// <summary>Explorer drops these into any folder, the way Finder drops .DS_Store.</summary>
    static bool IsSystemFile(string name) =>
        name.StartsWith('.') || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);

    /// <summary>Adds Linework's folders and files, changing nothing already there. The manifest goes last,
    /// so a failure can't leave something that looks like a library; anything made is removed again.</summary>
    public static void Create(string path, string weekOneStart, DateTimeOffset now)
    {
        var made = new List<string>();
        try
        {
            foreach (var name in Folders)
            {
                var folder = Path.Combine(path, name);
                var existed = Directory.Exists(folder);
                Directory.CreateDirectory(folder);
                if (!existed) made.Add(folder);
            }
            var files = new LibraryFiles(path);
            if (!File.Exists(files.PathOf(LibraryFile.Plan)))
                files.Write(LibraryFile.Plan, new PlanRecord(weekOneStart, []));
            var manifest = new LibraryManifest(LibraryManifest.CurrentFormat, Guid.NewGuid(), now);
            AtomicFile.Write(Path.Combine(path, ManifestName), LibraryJson.Encode(manifest));
        }
        catch
        {
            foreach (var folder in made)
                try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>The Monday of the week holding the date, as a day key.</summary>
    public static string MondayOf(DateOnly date) => PlanDayKey.Key(date.AddDays(-(((int)date.DayOfWeek + 6) % 7)));
}
