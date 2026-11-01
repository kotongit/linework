using Linework.Core.Library;

namespace Linework.App.Library;

public abstract record ChooserMessage;
public sealed record ChooserMissing(string Path) : ChooserMessage;
public sealed record ChooserNewerFormat : ChooserMessage;
public sealed record ChooserFailed(string Text) : ChooserMessage;

/// <summary>Which library is open, and how one gets opened. No library open means the chooser shows.</summary>
public sealed class AppModel(ILibraryHost host, Settings settings) : ObservableObject
{
    public ILibraryHost Host { get; } = host;
    public Settings Settings { get; } = settings;
    public LibraryLocation Location { get; } = new(settings);

    public LibraryStore? Library { get; private set; }
    public ChooserMessage? Message { get; set; }
    /// <summary>A folder with other things in it, waiting for the user to agree before Linework adds to it.</summary>
    public string? PendingAdoption { get; set; }

    public void OpenRemembered()
    {
        if (Location.Remembered is not { } path) return;
        if (LibraryLocation.DebugOverride != null && LibraryFolder.Classify(path) != FolderKind.Library)
        {
            Create(path);
            return;
        }
        Open(path);
    }

    /// <summary>Opens whatever the chosen folder turns out to be, asking first if it holds other things.</summary>
    public void Open(string path)
    {
        switch (LibraryFolder.Classify(path))
        {
            case FolderKind.Library: Load(path); break;
            case FolderKind.Empty: Create(path); break;
            case FolderKind.OtherContents: PendingAdoption = path; break;
            case FolderKind.Unavailable: Message = new ChooserMissing(path); break;
            case FolderKind.NewerFormat: Message = new ChooserNewerFormat(); break;
        }
        NotifyChanged();
    }

    /// <summary>A new name from the save dialog doesn't exist yet, so it's created rather than reported as missing.</summary>
    public void OpenCreating(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) Create(path);
        else Open(path);
    }

    public void Create(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            LibraryFolder.Create(path, LibraryFolder.MondayOf(Host.Today), Host.Now);
            Load(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Message = new ChooserFailed("Linework couldn't create a library there. Check that you can write to that folder, or choose another.");
        }
        NotifyChanged();
    }

    /// <summary>Saves everything. If that fails, asks before anything unsaved is left behind. Returns true if it's fine to go.</summary>
    public bool SaveOrConfirmLosing()
    {
        var saved = Library?.Flush() ?? true;
        return saved || Host.ConfirmLosingChanges();
    }

    public void CloseForSwitch()
    {
        if (!SaveOrConfirmLosing()) return;
        Library = null;
        Message = null;
        NotifyChanged();
    }

    void Load(string path)
    {
        try
        {
            Library = new LibraryStore(path, Host);
            Location.Remember(path);
            Message = null;
        }
        catch (NewerFormatException)
        {
            Message = new ChooserNewerFormat();
        }
        catch (UnreadableException)
        {
            Message = new ChooserFailed("Some of that library's files can't be read right now, so it wasn't opened. If it's in a synced folder, check that it has finished downloading.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Message = new ChooserFailed("Linework couldn't open that library.");
        }
        NotifyChanged();
    }
}
