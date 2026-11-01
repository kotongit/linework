using Linework.Core.Library;

namespace Linework.App.Library;

/// <summary>Notices photos added, renamed or removed in File Explorer.</summary>
public static class PhotoScanner
{
    public static async Task RunAsync(LibraryStore store, ILibraryHost host)
    {
        if (store.ImportsRunning != 0 || store.IsScanning) return;
        if (!store.PhotoRecordsLookComplete)
        {
            store.Notice = "Your photo records couldn't be read, so Linework isn't changing anything in Photos.";
            return;
        }
        store.IsScanning = true;
        try
        {
            var folder = store.PhotosDir;
            var records = store.Photos.ToList();
            var result = await Task.Run(() => Decide(records, folder));
            if (store.ImportsRunning != 0) return;
            var added = await ImportService.RecordsForExisting(result.Added.Select(n => Path.Combine(folder, n)).ToList(), host);
            store.ApplyScan(result, added);
        }
        finally
        {
            store.IsScanning = false;
        }
    }

    /// <summary>Set by OneDrive and other sync tools on a file whose contents are still in the cloud.</summary>
    const FileAttributes NotDownloaded = FileAttributes.Offline | (FileAttributes)0x00040000 | (FileAttributes)0x00400000;

    static PhotoScanResult Decide(IReadOnlyList<PhotoRecord> records, string folder)
    {
        var exists = Directory.Exists(folder);
        var files = new List<ScannedFile>();
        if (exists)
        {
            try
            {
                foreach (var info in new DirectoryInfo(folder).EnumerateFiles())
                    files.Add(new ScannedFile(info.Name, info.Length, (info.Attributes & NotDownloaded) != 0));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Out of reach just now, which reads as an empty folder and changes nothing.
                files.Clear();
            }
        }
        return PhotoScan.Decide(records, files, exists, name => FileHash.Sha256(Path.Combine(folder, name)));
    }
}
