using System.Globalization;
using System.Text;
using Linework.Core.Library;

namespace Linework.Core.Freehand;

public enum CanvasNameError { Invalid, Duplicate }

public sealed class CanvasNameException(CanvasNameError error) : Exception(error.ToString())
{
    public CanvasNameError Error { get; } = error;
}

/// <summary>The library's Canvases/ folder, where each canvas is a folder named after it.</summary>
public sealed class CanvasDirectory(string canvasesRoot)
{
    public string CanvasesRoot { get; } = canvasesRoot;
    internal Action<string, string> MoveFolder { get; set; } = Directory.Move;

    static readonly char[] Forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public string PathOf(string name) => Path.Combine(CanvasesRoot, name);

    /// <summary>Empty if the folder is out of reach, which a later save or switch will report.</summary>
    List<string> Folders()
    {
        try
        {
            return Directory.Exists(CanvasesRoot)
                ? Directory.EnumerateDirectories(CanvasesRoot).Select(p => Path.GetFileName(p)!).Where(n => !n.StartsWith('.')).ToList()
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public IReadOnlyList<string> List() => Folders().Where(IsUsableName).Order(Comparer<string>.Create(NaturalCompare)).ToList();

    /// <summary>Folders arriving from a Mac whose names Windows can't use. They're left alone and reported.</summary>
    public IReadOnlyList<string> UnusableNames() => Folders().Where(n => !IsUsableName(n)).ToList();

    /// <summary>The stricter of the two systems' rules.</summary>
    public static bool IsUsableName(string name)
    {
        if (name.Length == 0 || name.StartsWith('.') || name.EndsWith('.') || name.EndsWith(' ')) return false;
        if (name.IndexOfAny(Forbidden) >= 0 || name.Any(c => c < 0x20)) return false;
        var stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL")
            && !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9');
    }

    /// <summary>Numbers in order and case ignored, as Finder sorts: "Untitled 2" before "Untitled 10".</summary>
    public static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var x = a[si..i].TrimStart('0');
                var y = b[sj..j].TrimStart('0');
                var numbers = x.Length != y.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y);
                if (numbers != 0) return numbers;
            }
            else
            {
                int si = i, sj = j;
                while (i < a.Length && !char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && !char.IsAsciiDigit(b[j])) j++;
                var text = string.Compare(a[si..i], b[sj..j], CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
                if (text != 0) return text;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    static bool Same(string a, string b) => PhotoScan.Comparable(a) == PhotoScan.Comparable(b);

    static string Clean(string name)
    {
        var trimmed = name.Trim();
        if (!IsUsableName(trimmed)) throw new CanvasNameException(CanvasNameError.Invalid);
        return trimmed.Normalize(NormalizationForm.FormC);
    }

    string NextFree(string baseName)
    {
        var existing = Folders().ToList();
        if (!existing.Any(n => Same(n, baseName))) return baseName;
        var number = 2;
        while (existing.Any(n => Same(n, $"{baseName} {number}"))) number++;
        return $"{baseName} {number}";
    }

    /// <summary>A null name picks the next free "Untitled".</summary>
    public string Create(string? name)
    {
        var chosen = name != null ? Clean(name) : NextFree("Untitled");
        if (Folders().Any(n => Same(n, chosen))) throw new CanvasNameException(CanvasNameError.Duplicate);
        // If the library has gone, this fails rather than making a new one.
        if (!Directory.Exists(CanvasesRoot)) throw new DirectoryNotFoundException(CanvasesRoot);
        Directory.CreateDirectory(PathOf(chosen));
        return chosen;
    }

    public string Rename(string old, string name)
    {
        var chosen = Clean(name);
        if (chosen == old) return chosen;
        if (Folders().Any(n => Same(n, chosen) && !Same(n, old))) throw new CanvasNameException(CanvasNameError.Duplicate);
        if (Same(old, chosen))
        {
            // The disk sees these as one name, so it takes two steps.
            var step = PathOf($".renaming-{Guid.NewGuid()}");
            FileRetry.Run(() => MoveFolder(PathOf(old), step));
            try
            {
                FileRetry.Run(() => MoveFolder(step, PathOf(chosen)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Put it back under its old name, or it would look lost.
                MoveFolder(step, PathOf(old));
                throw;
            }
        }
        else
        {
            FileRetry.Run(() => MoveFolder(PathOf(old), PathOf(chosen)));
        }
        return chosen;
    }

    public string Duplicate(string name)
    {
        var copy = NextFree($"{name} copy");
        try
        {
            CopyFolder(PathOf(name), PathOf(copy));
        }
        catch
        {
            try { Directory.Delete(PathOf(copy), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        return copy;
    }

    static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var folder in Directory.EnumerateDirectories(from)) CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
    }

    /// <summary>A library always has a canvas. Returns the first, making one if needed.</summary>
    public string EnsureOne() => List().FirstOrDefault() ?? Create(null);
}
