using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Linework.App.Library;
using Linework.App.Views;

namespace Linework.App.Platform;

/// <summary>The stores' system services, on Windows.</summary>
public sealed class WpfHost : ILibraryHost
{
    public IDisposable After(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
        return new Stopper(timer);
    }

    sealed class Stopper(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }

    public void MoveToRecycleBin(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            if (RecycleBin.TryMove(path)) continue;
            var name = Path.GetFileName(path);
            if (AlertWindow.Ask($"“{name}” can't go to the Recycle Bin.", "The drive it's on may not have a Recycle Bin. Deleting it now can't be undone.",
                    "Delete Permanently", "Cancel") != 0)
                throw new OperationCanceledException();
            File.Delete(path);
        }
    }

    public bool ConfirmLosingChanges() =>
        AlertWindow.Ask("Some changes couldn't be saved.", "Your library's folder may be unavailable. If you continue, those changes will be lost.", "Stay", "Continue Anyway") == 1;

    public bool IsReadableImage(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames.Count > 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or COMException or InvalidOperationException)
        {
            return false;
        }
    }

    public (Linework.Core.Crop.NormalizedRect Rect, bool Found) AutoCrop(string path) => (Linework.Core.Crop.CropMath.Unit, false);

    public DateTimeOffset Now
    {
        get
        {
            var today = DebugToday;
            return today != null ? new DateTimeOffset(today.Value.ToDateTime(new TimeOnly(12, 0)), DateTimeOffset.Now.Offset) : DateTimeOffset.Now;
        }
    }

    public DateOnly Today => DebugToday ?? DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Debug builds can pretend it's another day with LINEWORK_TODAY=yyyy-MM-dd.</summary>
    static DateOnly? DebugToday
    {
        get
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("LINEWORK_TODAY") is { } key) return Linework.Core.Plan.PlanDayKey.Date(key);
#endif
            return null;
        }
    }
}

/// <summary>Files in the library are the user's own, so deleting moves them to the Recycle Bin.</summary>
public static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    const uint FO_DELETE = 3;
    const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>False if there's no Recycle Bin for it, or the move failed. A network or removable drive has none, and the shell would erase instead.</summary>
    public static bool TryMove(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(full)!).DriveType != DriveType.Fixed) return false;
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = full + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
        };
        return SHFileOperation(ref op) == 0 && op.fAnyOperationsAborted == 0 && !File.Exists(full);
    }
}
