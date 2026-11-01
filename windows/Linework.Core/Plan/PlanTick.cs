namespace Linework.Core.Plan;

/// <summary>One ticked block of one lesson. CountsOn is the day it adds to the streak and calendar.</summary>
public sealed record PlanTick(string Key, string CountsOn, DateTimeOffset CheckedAt)
{
    /// <summary>The lesson's scheduled day or today, whichever is earlier: filling in a missed day counts on that day,
    /// and working ahead counts today, never on a day that hasn't happened.</summary>
    public static string CountsOnFor(DateOnly scheduled, DateOnly today) => PlanDayKey.Key(scheduled < today ? scheduled : today);
}
