using System.Runtime.InteropServices;
using System.Windows.Media;
using Linework.App.Library;

namespace Linework.App.Platform;

public static class KeepAwake
{
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

    const uint Continuous = 0x80000000, SystemRequired = 1, DisplayRequired = 2;

    /// <summary>While a session runs, drawing on another device counts as nothing, so the screen is told to stay on.</summary>
    public static bool IsHeld { get; private set; }

    /// <summary>Call on the UI thread. Disposing releases it.</summary>
    public static IDisposable Begin()
    {
        IsHeld = SetThreadExecutionState(Continuous | SystemRequired | DisplayRequired) != 0;
        return new Release();
    }

    sealed class Release : IDisposable
    {
        bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            SetThreadExecutionState(Continuous);
            IsHeld = false;
        }
    }
}

/// <summary>Short original sounds bundled with the app. Each plays on its own, so overlapping cues don't cut each other off.</summary>
public static class SoundCue
{
    public static bool Muted { get; set; }

    public static void Tick() => Play("tick.wav", 0.5);
    public static void Chime() => Play("chime.wav", 0.8);
    /// <summary>s.e.a. has something to say. Different from Chime, which means the pose changed.</summary>
    public static void Note() => Play("note.wav", 0.4);
    public static void Celebrate() => Play("celebrate.wav", 0.8);

    static void Play(string name, double volume)
    {
        if (Muted) return;
        var player = new MediaPlayer { Volume = volume };
        player.MediaEnded += (_, _) => player.Close();
        player.MediaFailed += (_, _) => player.Close();
        player.Open(new Uri(Path.Combine(AppContext.BaseDirectory, "Resources", "Sounds", name)));
        player.Play();
    }
}

public sealed class WpfSessionEffects : ISessionEffects
{
    public IDisposable KeepAwake() => Platform.KeepAwake.Begin();
    public void Tick() => SoundCue.Tick();
    public void Chime() => SoundCue.Chime();
    public void Note() => SoundCue.Note();
}
