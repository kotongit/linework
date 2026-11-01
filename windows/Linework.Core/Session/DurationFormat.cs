using System.Globalization;

namespace Linework.Core.Session;

public static class DurationFormat
{
    static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    /// <summary>Pose length label: "30s", "1 min", "1m 30s".</summary>
    public static string Short(int seconds)
    {
        int m = seconds / 60, s = seconds % 60;
        if (m == 0) return F($"{s}s");
        return s == 0 ? F($"{m} min") : F($"{m}m {s}s");
    }

    /// <summary>Countdown display, rounding up so a pose never shows 0:00 while running.</summary>
    public static string Clock(double seconds)
    {
        var total = Math.Max((int)Math.Ceiling(seconds), 0);
        return F($"{total / 60}:{total % 60:00}");
    }

    /// <summary>Session total: "20s", "18 min", "1 h 5 min".</summary>
    public static string Total(int seconds)
    {
        if (seconds < 60) return F($"{seconds}s");
        var minutes = seconds / 60;
        return minutes < 60 ? F($"{minutes} min") : F($"{minutes / 60} h {minutes % 60} min");
    }
}
