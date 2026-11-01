using System.Text.Json;
using Linework.Core;
using Linework.Core.Library;

namespace Linework.App.Library;

/// <summary>Preferences, kept in %APPDATA%\Linework\settings.json. A null path keeps them in memory, for the harness.</summary>
public sealed class Settings : ObservableObject
{
    public sealed record Values
    {
        public string Name { get; init; } = "";
        public bool Welcomed { get; init; }
        public bool Clock24h { get; init; } = true;
        public bool ClockSeconds { get; init; } = true;
        public bool Notes { get; init; } = true;
        public double Transparency { get; init; } = Glass.DefaultTransparency;
        public string? Library { get; init; }
        /// <summary>The canvas each library last had open.</summary>
        public Dictionary<string, string> LastCanvas { get; init; } = [];
        public bool Sound { get; init; } = true;
        public bool SetupGrayscale { get; init; }
        public bool SetupClassMode { get; init; }
        public int SetupSeconds { get; init; } = 60;
        public int SetupMaxPoses { get; init; }
        public bool SetupShuffle { get; init; } = true;
        public bool SetupToday { get; init; }
        public int SetupTargetMinutes { get; init; } = Linework.Core.Session.SessionLength.DefaultTargetSeconds / 60;
    }

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    readonly string? _path;
    public Values Current { get; private set; } = new();

    public Settings(string? path)
    {
        _path = path;
        if (path == null || !File.Exists(path)) return;
        try
        {
            Current = JsonSerializer.Deserialize<Values>(File.ReadAllBytes(path), Options) ?? new();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Preferences can always be set again.
        }
    }

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Linework", "settings.json");

    public void Update(Func<Values, Values> change)
    {
        var next = change(Current);
        if (next == Current) return;
        Current = next;
        if (_path != null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                AtomicFile.Write(_path, JsonSerializer.SerializeToUtf8Bytes(next, Options));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Stays for this run; the next change tries again.
            }
        }
        NotifyChanged();
    }
}
