using System.Globalization;

namespace Linework.Core;

public sealed record ClockParts(string Time, string Seconds, string? Meridiem);

/// <summary>Built from time components with zero padding, so it never depends on locale formatting.</summary>
public static class ClockFormat
{
    public static ClockParts Parts(TimeOnly time, bool use24h)
    {
        var hour = time.Hour;
        var shown = use24h ? hour : hour % 12 == 0 ? 12 : hour % 12;
        return new ClockParts($"{Pad(shown)}:{Pad(time.Minute)}", Pad(time.Second), use24h ? null : hour < 12 ? "AM" : "PM");
    }

    static string Pad(int value) => value.ToString("00", CultureInfo.InvariantCulture);
}
