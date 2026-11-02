using System.Text;

namespace Linework.Core.Library;

/// <summary>IsPlaceholder: in a synced folder and not yet downloaded.</summary>
public sealed record ScannedFile(string Name, long Size, bool IsPlaceholder);

public sealed class PhotoScanResult
{
    public List<string> Added { get; } = [];
    public List<(Guid RecordID, string NewName)> Adopted { get; } = [];
    public List<Guid> Missing { get; } = [];
    public List<Guid> Restored { get; } = [];
    /// <summary>Photos/ is absent or empty while photos are on record. Treated as out of reach, never as deletion.</summary>
    public bool FolderUnavailable { get; set; }
}

/// <summary>What changes made in File Explorer mean. Pure logic: the caller lists the folder and applies the result.</summary>
public static class PhotoScan
{
    static readonly HashSet<string> ImageExtensions = ["jpg", "jpeg", "png", "heic", "heif", "webp", "tif", "tiff", "gif", "bmp"];

    public static bool IsImageName(string name)
    {
        var (_, extension) = Split(name);
        return ImageExtensions.Contains(extension.ToLowerInvariant());
    }

    /// <summary>The stem and extension as Foundation splits them: a leading dot doesn't start an extension.</summary>
    static (string Stem, string Extension) Split(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? (name, "") : (name[..dot], name[(dot + 1)..]);
    }

    /// <summary>A Mac may store "é" decomposed, and Windows treats the two forms as different names, so names are compared in one form.</summary>
    public static string Comparable(string name) => name.Normalize(NormalizationForm.FormC).ToLowerInvariant();

    public static PhotoScanResult Decide(IReadOnlyList<PhotoRecord> records, IReadOnlyList<ScannedFile> files, bool folderExists, Func<string, string> hash)
    {
        var result = new PhotoScanResult();
        var visible = files.Where(f => !f.Name.StartsWith('.') && IsImageName(f.Name)).ToList();
        if (records.Count > 0 && (!folderExists || visible.Count == 0))
        {
            result.FolderUnavailable = true;
            return result;
        }
        var names = visible.Select(f => Comparable(f.Name)).ToHashSet();
        var known = records.Select(r => Comparable(r.FileName)).ToHashSet();
        var gone = new List<PhotoRecord>();
        foreach (var record in records)
        {
            if (names.Contains(Comparable(record.FileName)))
            {
                if (record.Missing) result.Restored.Add(record.Id);
            }
            else
            {
                gone.Add(record);
            }
        }
        foreach (var file in visible)
        {
            if (known.Contains(Comparable(file.Name)) || file.IsPlaceholder) continue;
            // Hashing is only worth it if the new file might be a renamed one.
            var index = -1;
            if (gone.Count > 0)
            {
                try
                {
                    var digest = hash(file.Name);
                    index = gone.FindIndex(r => r.Sha256 == digest);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            if (index >= 0)
            {
                result.Adopted.Add((gone[index].Id, file.Name));
                gone.RemoveAt(index);
            }
            else
            {
                result.Added.Add(file.Name);
            }
        }
        result.Missing.AddRange(gone.Where(r => !r.Missing).Select(r => r.Id));
        return result;
    }

    /// <summary>False if a set or benchmark points at a photo with no record, which means the records were lost.
    /// Adding files then would give them new identities and strand everything that points at the old ones.</summary>
    public static bool RecordsLookComplete(IReadOnlyList<PhotoRecord> records, IReadOnlySet<Guid> referenced) =>
        referenced.IsSubsetOf(records.Select(r => r.Id));

    /// <summary>The wanted name, or "name 2.ext" and so on if it's taken. Compared the way the disk compares names.</summary>
    public static string UniqueName(string wanted, IEnumerable<string> taken)
    {
        var used = taken.Select(Comparable).ToHashSet();
        if (!used.Contains(Comparable(wanted))) return wanted;
        var (stem, extension) = Split(wanted);
        var suffix = extension.Length == 0 ? "" : "." + extension;
        var number = 2;
        while (used.Contains(Comparable($"{stem} {number}{suffix}"))) number++;
        return $"{stem} {number}{suffix}";
    }

    /// <summary>The path of a named file in a folder, finding it in whichever Unicode form it's stored. Falls back to the plain path.</summary>
    public static string PathOnDisk(string folder, string name)
    {
        var plain = Path.Combine(folder, name);
        if (File.Exists(plain) || !Directory.Exists(folder)) return plain;
        var wanted = Comparable(name);
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder))
                if (Comparable(Path.GetFileName(path)) == wanted) return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Out of reach: whatever opens the path will say so.
        }
        return plain;
    }
}
