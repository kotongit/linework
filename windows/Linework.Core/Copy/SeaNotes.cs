using System.Globalization;

namespace Linework.Core.Copy;

/// <summary>Per-visit memory for the notes: which general lines were used lately, and which time notes were shown.</summary>
public sealed class SeaNoteState
{
    public List<string> RecentGeneral { get; } = [];
    public HashSet<string> ShownTimeNotes { get; } = [];
}

/// <summary>Per-session memory for the notes in a timed session.</summary>
public sealed class PracticeNoteState
{
    public HashSet<string> ShownMilestones { get; } = [];
    public List<string> RecentLines { get; } = [];
}

/// <summary>Fixed lines that s.e.a. says while Freehand or a timed session is open. Pure logic, nothing is saved.</summary>
public static class SeaNotes
{
    public const int FirstDelay = 300;
    public const int VisibleSeconds = 8;

    public static readonly IReadOnlyList<string> GeneralLines =
    [
        "You can do it.",
        "What are we drawing today?",
        "Are we there yet?",
        "Loosen your shoulders.",
        "Don't forget to blink.",
        "Have some water. I'll wait.",
        "That line was braver than you think.",
        "Ugly drawings count too.",
        "Look at it upside down. Trust me.",
        "I like this one.",
        "Still with you, {name}.",
    ];

    public static readonly IReadOnlyList<string> PracticeLines =
    [
        "You can do it.",
        "Keep your hand moving.",
        "You're doing it.",
        "Loose lines win.",
        "Ugly drawings count too.",
        "That line was braver than you think.",
        "Breathe. Then the next line.",
        "Still with you, {name}.",
    ];

    public const int PracticeFirstDelay = 120;
    public const int PracticeVisibleSeconds = 6;

    const int RepeatWindow = 5;
    const int TwentyMinutes = 20 * 60;
    const int SixtyMinutes = 60 * 60;
    const string UsualKey = "usual", TwentyKey = "20", SixtyKey = "60";

    /// <summary>"25 minutes", "1 hour", "1 hour 10 minutes". Minutes round down.</summary>
    public static string TimeText(int seconds)
    {
        var total = Math.Max(0, seconds) / 60;
        int hours = total / 60, minutes = total % 60;
        static string Unit(int n, string word) => n.ToString(CultureInfo.InvariantCulture) + " " + word + (n == 1 ? "" : "s");
        if (hours == 0) return Unit(minutes, "minute");
        if (minutes == 0) return Unit(hours, "hour");
        return $"{Unit(hours, "hour")} {Unit(minutes, "minute")}";
    }

    /// <summary>Average session length; null until there are at least 3 sessions.</summary>
    public static int? UsualSeconds(IReadOnlyList<int> sessionSeconds) =>
        sessionSeconds.Count < 3 ? null : sessionSeconds.Sum() / sessionSeconds.Count;

    /// <summary>Seconds until the next note after one has appeared: 600...1200.</summary>
    public static int NextInterval(Random rng) => rng.Next(600, 1201);

    /// <summary>The next note for this visit. Time notes take priority (usual, then 60 minutes, then 20 minutes).</summary>
    public static string Next(int visitSeconds, string name, int? usualSeconds, SeaNoteState state, Random rng)
    {
        var time = TimeText(visitSeconds);
        if (usualSeconds is { } usual && visitSeconds > usual && !state.ShownTimeNotes.Contains(UsualKey))
        {
            state.ShownTimeNotes.UnionWith([UsualKey, TwentyKey]);
            return $"You've been drawing for {time}. That's longer than your usual practice.";
        }
        if (visitSeconds >= SixtyMinutes && !state.ShownTimeNotes.Contains(SixtyKey))
        {
            state.ShownTimeNotes.UnionWith([TwentyKey, SixtyKey]);
            return $"{time} already. Maybe stretch for a second?";
        }
        if (visitSeconds >= TwentyMinutes && state.ShownTimeNotes.Add(TwentyKey))
            return $"You've been drawing for {time}.";
        return PickLine(GeneralLines, name, state.RecentGeneral, rng);
    }

    /// <summary>The note for reaching a pose, once per session: the first, halfway (in longer sessions), three left, the last.</summary>
    public static string? PracticeMilestone(int poseIndex, int poseCount, PracticeNoteState state)
    {
        var left = poseCount - poseIndex;
        string line;
        if (poseIndex == 0) line = "You just began, you can do it.";
        else if (left == 1) line = "Last one. Make it count.";
        else if (left == 3) line = "Only 3 left.";
        else if (poseCount >= 8 && poseIndex == poseCount / 2) line = "Halfway there. Keep going.";
        else return null;
        return state.ShownMilestones.Add(line) ? line : null;
    }

    /// <summary>Seconds until the next encouragement in a session: 300...480.</summary>
    public static int NextPracticeInterval(Random rng) => rng.Next(300, 481);

    /// <summary>Encouragement for the stretches between milestones, like a long pose.</summary>
    public static string PracticeLine(string name, PracticeNoteState state, Random rng) =>
        PickLine(PracticeLines, name, state.RecentLines, rng);

    static string PickLine(IReadOnlyList<string> lines, string name, List<string> recent, Random rng)
    {
        var cleanName = LineworkCopy.NormalizedName(name);
        var candidates = lines.Where(l => (cleanName.Length > 0 || !l.Contains("{name}")) && !recent.Contains(l)).ToList();
        var pick = candidates.Count > 0 ? candidates[rng.Next(candidates.Count)] : lines[0];
        recent.Add(pick);
        if (recent.Count > RepeatWindow) recent.RemoveRange(0, recent.Count - RepeatWindow);
        return pick.Replace("{name}", cleanName);
    }
}
