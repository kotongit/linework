namespace Linework.App.Library;

/// <summary>What the stores need from the system. Windows gives the real thing; tests give a stand-in.</summary>
public interface ILibraryHost
{
    /// <summary>Runs the action later on the UI thread, unless disposed first.</summary>
    IDisposable After(TimeSpan delay, Action action);
    /// <summary>Files and folders already gone are fine. Throws at the first that can't be moved, unless the user agrees to erase it.</summary>
    void MoveToRecycleBin(IReadOnlyList<string> paths);
    /// <summary>Asks before anything unsaved is left behind. True to go on regardless.</summary>
    bool ConfirmLosingChanges();
    bool IsReadableImage(string path);
    /// <summary>Pixel size as displayed (EXIF orientation applied), read from the header without decoding.</summary>
    (double Width, double Height)? OrientedPixelSize(string path);
    /// <summary>Finds the figure(s) in a file, off the UI thread. Falls back to the full frame.</summary>
    (Linework.Core.Crop.NormalizedRect Rect, bool Found) AutoCrop(string path);
    DateTimeOffset Now { get; }
    DateOnly Today { get; }
}
