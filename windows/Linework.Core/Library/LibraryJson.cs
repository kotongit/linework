using System.Globalization;

namespace Linework.Core.Library;

public static partial class LibraryJson
{
    /// <summary>A number the way Swift's encoder writes it: shortest round-trip form, no ".0", lowercase exponent.</summary>
    public static string FormatNumber(double value)
    {
        if (value == Math.Floor(value) && Math.Abs(value) < 9007199254740992d)
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("R", CultureInfo.InvariantCulture).Replace("E", "e", StringComparison.Ordinal);
    }
}
