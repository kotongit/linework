using System.Text.Json;
using Linework.Core.Plan;

namespace Linework.Core.Library;

public sealed class NewerFormatException(string file) : Exception($"{file} was made by a newer Linework.")
{
    public string File { get; } = file;
}

/// <summary>It's there but can't be read: an undownloaded file while offline, a disk error, a permission problem, or a lock that didn't clear.</summary>
public sealed class UnreadableException(string file, Exception? inner = null) : Exception($"{file} can't be read.", inner)
{
    public string File { get; } = file;
}

/// <summary>The library's folder isn't there, for example on an unplugged drive.</summary>
public sealed class LibraryUnavailableException() : Exception("The library's folder isn't there.");

/// <summary>One text file in a library, and what it holds.</summary>
public sealed record LibraryFile<T>(string Name);

public static class LibraryFile
{
    public static readonly LibraryFile<IReadOnlyList<PhotoRecord>> Photos = new("photos.json");
    public static readonly LibraryFile<BenchmarksFile> Benchmarks = new("benchmarks.json");
    public static readonly LibraryFile<IReadOnlyList<SetRecord>> Sets = new("sets.json");
    public static readonly LibraryFile<IReadOnlyList<PresetRecord>> Presets = new("presets.json");
    public static readonly LibraryFile<IReadOnlyList<PlanTick>> Checks = new("checks.json");
    public static readonly LibraryFile<PlanRecord> Plan = new("plan.json");
    public static readonly LibraryFile<IReadOnlyList<SessionRecord>> Sessions = new("sessions.json");
}

public abstract record LoadResult<T>;
public sealed record Missing<T> : LoadResult<T>;
public sealed record Loaded<T>(T Value) : LoadResult<T>;
public sealed record Damaged<T>(string SetAsideAs) : LoadResult<T>;

/// <summary>Antivirus, indexers and sync tools hold files for a moment. Waits about 2 s for them, then lets the failure through.</summary>
public static class FileRetry
{
    static readonly TimeSpan Window = TimeSpan.FromSeconds(2);
    static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(100);

    public static void Run(Action action) => Run(() => { action(); return true; });

    public static T Run<T>(Func<T> action)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            try
            {
                return action();
            }
            catch (Exception e) when (IsWorthRetrying(e) && DateTime.UtcNow - started < Window)
            {
                Thread.Sleep(Pause);
            }
        }
    }

    static bool IsWorthRetrying(Exception e) =>
        e is UnauthorizedAccessException || (e is IOException && e is not (FileNotFoundException or DirectoryNotFoundException or PathTooLongException));
}

public static class AtomicFile
{
    /// <summary>Writes beside the target, then moves into place. On failure the original stays as it was and no temporary file is left.</summary>
    public static void Write(string path, byte[] data)
    {
        var folder = Path.GetDirectoryName(path)!;
        var temp = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(true);
            }
            FileRetry.Run(() => File.Move(temp, path, true));
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}

/// <summary>Reads and writes the text files under a library's Progress/ folder.</summary>
public sealed class LibraryFiles(string root)
{
    public const int CurrentVersion = 1;
    public string Root { get; } = root;

    sealed record Envelope<T>(int Version, T Items);

    /// <summary>Beside the path, with "damaged" in its name. Never one that's already there, so nothing set aside gets replaced.</summary>
    public static string FreeAsideName(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var taken = Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>().ToList();
        return Path.Combine(folder, PhotoScan.UniqueName($"{stem}.damaged.json", taken));
    }

    string Progress => Path.Combine(Root, "Progress");

    public string PathOf<T>(LibraryFile<T> file) => Path.Combine(Progress, file.Name);

    public DateTime? ModificationDate<T>(LibraryFile<T> file)
    {
        var path = PathOf(file);
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }

    public LoadResult<T> Read<T>(LibraryFile<T> file)
    {
        var path = PathOf(file);
        if (!File.Exists(path)) return new Missing<T>();
        byte[] data;
        try
        {
            data = FileRetry.Run(() => File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new UnreadableException(file.Name, e);
        }
        if (VersionIn(data) > CurrentVersion) throw new NewerFormatException(file.Name);
        try
        {
            var envelope = LibraryJson.Decode<Envelope<T>>(data);
            if (envelope.Items is not null) return new Loaded<T>(envelope.Items);
        }
        catch (JsonException) { }
        var aside = FreeAsideName(path);
        try
        {
            FileRetry.Run(() => File.Move(path, aside));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new UnreadableException(file.Name, e);
        }
        return new Damaged<T>(aside);
    }

    static int VersionIn(byte[] data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("version", out var v) && v.TryGetInt32(out var version) ? version : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>Atomic. Never recreates a library that has gone away.</summary>
    public void Write<T>(LibraryFile<T> file, T value)
    {
        if (!Directory.Exists(Root)) throw new LibraryUnavailableException();
        Directory.CreateDirectory(Progress);
        AtomicFile.Write(PathOf(file), LibraryJson.Encode(new Envelope<T>(CurrentVersion, value)));
    }
}
