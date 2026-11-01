using System.Globalization;
using Linework.Core.Plan;

namespace Linework.Core.Copy;

/// <summary>Every line the app says to the user, in one place so the voice stays consistent and tested.</summary>
public static class LineworkCopy
{
    public const string UnderGreeting = "A few minutes of practice a day keeps s.e.a. growing — and your hand steady.";
    public const string FreehandEmpty = "Your canvas is empty. Drag your images here.";
    public const string RestDay = "Rest today. You've earned it.";
    public const string NoLessonThatDay = "No lesson that day.";
    public const string EmptyLibrary = "Drag your photos here";
    public const string EmptyBenchmarks = "Right-click a photo to make it your benchmark.";
    public const string SessionTooShort = "That one was too short to count on your calendar.";

    public const int MaxNameLength = 40;

    /// <summary>Hours 5–11 are morning, 12–18 afternoon, the rest evening.</summary>
    public static string Greeting(int hour, string name)
    {
        var part = hour switch { >= 5 and <= 11 => "morning", >= 12 and <= 18 => "afternoon", _ => "evening" };
        var who = name.Length == 0 ? "" : $", {name}";
        return $"Good {part}{who}. Take care of s.e.a.";
    }

    public static string Celebrate(Celebration kind, bool isToday = true) => kind switch
    {
        Celebration.Day => isToday ? "You finished today." : "You finished that day.",
        Celebration.Week => isToday ? "You finished this week." : "You finished that week.",
        _ => isToday ? "You finished this book." : "You finished that book.",
    };

    public static string NormalizedName(string raw)
    {
        var trimmed = raw.Trim();
        var info = new StringInfo(trimmed);
        return info.LengthInTextElements <= MaxNameLength ? trimmed : info.SubstringByTextElements(0, MaxNameLength);
    }

    public static bool ShouldShowWelcome(string name, bool welcomed) => name.Length == 0 && !welcomed;
}
