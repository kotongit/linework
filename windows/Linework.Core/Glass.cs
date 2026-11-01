namespace Linework.Core;

/// <summary>The window's see-through setting: transparency is what the user picks, tint opacity is what gets drawn.</summary>
public static class Glass
{
    public const double DefaultTransparency = 0.33;
    public const double MinTransparency = 0.0, MaxTransparency = 0.8;

    public static double TintOpacity(double transparency) => 1 - Math.Min(Math.Max(transparency, MinTransparency), MaxTransparency);
}
