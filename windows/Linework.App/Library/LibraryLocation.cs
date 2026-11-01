namespace Linework.App.Library;

/// <summary>Remembers which library was open. Windows has no bookmarks, so a moved folder is reported by the chooser and asked for again.</summary>
public sealed class LibraryLocation(Settings settings)
{
    public static string? DebugOverride
    {
        get
        {
#if DEBUG
            return Environment.GetEnvironmentVariable("LINEWORK_LIBRARY");
#else
            return null;
#endif
        }
    }

    public string? Remembered => DebugOverride ?? settings.Current.Library;

    public bool HasRemembered => Remembered != null;

    public void Remember(string path)
    {
        if (DebugOverride == null) settings.Update(v => v with { Library = path });
    }
}
