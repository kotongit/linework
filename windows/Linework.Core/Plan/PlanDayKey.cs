using System.Globalization;

namespace Linework.Core.Plan;

/// <summary>Calendar-date keys ("yyyy-MM-dd") for storing progress per day.</summary>
public static class PlanDayKey
{
    public static string Key(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly? Date(string key)
    {
        var parts = key.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var numbers = new List<int>();
        foreach (var part in parts)
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) numbers.Add(n);
        if (numbers.Count != 3) return null;
        var (y, m, d) = (numbers[0], numbers[1], numbers[2]);
        if (y is < 1 or > 9999 || m is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(y, m)) return null;
        return new DateOnly(y, m, d);
    }
}
