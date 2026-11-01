using System.Windows.Media;
using Microsoft.Win32;

namespace Linework.App.Theme;

/// <summary>Linework design tokens. The brushes are shared and change colour in place, so every view follows the theme.</summary>
public static class Tokens
{
    public static readonly SolidColorBrush Ink = new(), Muted = new(), Hairline = new(), Ground = new(), Teal = new();
    public static bool IsDark { get; private set; }
    public static event Action? Changed;

    static Color Hex(uint value, double alpha = 1) =>
        Color.FromArgb((byte)Math.Round(alpha * 255), (byte)(value >> 16), (byte)(value >> 8), (byte)value);

    const uint LightInk = 0x111111, DarkInk = 0xF4F4F2, LightGround = 0xF4F4F2, DarkGround = 0x0E0E0E;

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var ink = dark ? DarkInk : LightInk;
        Ink.Color = Hex(ink);
        // Ink at 63%, so it keeps its contrast over whatever shows through the glass.
        Muted.Color = Hex(ink, 0.63);
        Hairline.Color = Hex(ink, 0.18);
        Ground.Color = Hex(dark ? DarkGround : LightGround);
        Teal.Color = Hex(dark ? 0x40CBE0u : 0x30B0C7u);
        Changed?.Invoke();
    }

    public static bool SystemIsDark()
    {
        var value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
        return value is int light && light == 0;
    }

    public static readonly FontFamily Font = new(new Uri("pack://application:,,,/"), "./Resources/Fonts/#Inter Tight");
    public static readonly FontFamily Icons = new("Segoe MDL2 Assets");
}
