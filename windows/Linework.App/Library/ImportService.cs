using Linework.Core.Library;

namespace Linework.App.Library;

/// <summary>Brings photos into a library: copied in under readable names, hashed and auto-cropped, a few at a time.</summary>
public static class ImportService
{
    const int MaxConcurrent = 4;

    /// <summary>Unreadable files are skipped.</summary>
    public static async Task<(int Imported, int Skipped)> ImportAsync(IReadOnlyList<string> paths, LibraryStore store, ILibraryHost host, Guid? setId)
    {
        store.ImportsRunning++;
        try
        {
            // Names are settled here, before any copying starts, so two files can't claim one.
            // A missing photo keeps its name, so restoring its file can't land on a different picture.
            var taken = (Directory.Exists(store.PhotosDir) ? Directory.EnumerateFileSystemEntries(store.PhotosDir).Select(p => Path.GetFileName(p)!) : []).ToList();
            taken.AddRange(store.Photos.Select(p => p.FileName));
            var jobs = new List<(string Source, string Target)>();
            foreach (var path in paths)
            {
                var name = PhotoScan.UniqueName(Path.GetFileName(path), taken);
                taken.Add(name);
                jobs.Add((path, Path.Combine(store.PhotosDir, name)));
            }
            var records = await Prepare(jobs, job => CopyIn(job.Source, job.Target, host));
            store.AddPhotos(records, setId);
            return (records.Count, paths.Count - records.Count);
        }
        finally
        {
            store.ImportsRunning--;
        }
    }

    /// <summary>Records for files already in Photos/, found by the File Explorer check.</summary>
    public static Task<List<PhotoRecord>> RecordsForExisting(IReadOnlyList<string> paths, ILibraryHost host) =>
        Prepare(paths.Select(p => (p, p)).ToList(), job => Describe(job.Item2, host));

    /// <summary>In the order given. Work that fails just leaves that file out.</summary>
    static async Task<List<PhotoRecord>> Prepare(IReadOnlyList<(string, string)> jobs, Func<(string Source, string Target), PhotoRecord> work)
    {
        using var gate = new SemaphoreSlim(MaxConcurrent);
        var tasks = jobs.Select(job => Task.Run(async () =>
        {
            await gate.WaitAsync();
            try
            {
                return work(job);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return null;
            }
            finally
            {
                gate.Release();
            }
        })).ToList();
        var results = await Task.WhenAll(tasks);
        return results.OfType<PhotoRecord>().ToList();
    }

    static PhotoRecord CopyIn(string source, string target, ILibraryHost host)
    {
        if (!host.IsReadableImage(source)) throw new InvalidDataException("Not an image");
        File.Copy(source, target);
        try
        {
            return Describe(target, host);
        }
        catch
        {
            try { File.Delete(target); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>Hashing, decoding and detection, all off the UI thread.</summary>
    static PhotoRecord Describe(string path, ILibraryHost host)
    {
        if (!host.IsReadableImage(path)) throw new InvalidDataException("Not an image");
        var hash = FileHash.Sha256(path);
        var (rect, found) = host.AutoCrop(path);
        return new PhotoRecord(Guid.NewGuid(), Path.GetFileName(path), hash, host.Now, rect, 0, false, !found, false);
    }
}
