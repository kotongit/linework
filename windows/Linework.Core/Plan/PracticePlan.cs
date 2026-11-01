using System.Text.Json;
using System.Text.Json.Serialization;
using Linework.Core.Session;

namespace Linework.Core.Plan;

[JsonConverter(typeof(PlanBlockKindConverter))]
public enum PlanBlockKind { Warmup, Focus, Check, Exercise, BlockA, BlockB, Ritual }

public static class PlanBlockKinds
{
    public static string Raw(this PlanBlockKind kind) => kind switch
    {
        PlanBlockKind.Warmup => "warmup",
        PlanBlockKind.Focus => "focus",
        PlanBlockKind.Check => "check",
        PlanBlockKind.Exercise => "exercise",
        PlanBlockKind.BlockA => "blockA",
        PlanBlockKind.BlockB => "blockB",
        _ => "ritual",
    };

    public static PlanBlockKind Parse(string raw) =>
        Enum.GetValues<PlanBlockKind>().First(k => k.Raw() == raw);
}

sealed class PlanBlockKindConverter : JsonConverter<PlanBlockKind>
{
    public override PlanBlockKind Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o) =>
        PlanBlockKinds.Parse(reader.GetString() ?? throw new JsonException());
    public override void Write(Utf8JsonWriter writer, PlanBlockKind value, JsonSerializerOptions o) => writer.WriteStringValue(value.Raw());
}

/// <summary>Timed is the steps for a session from photos; null for work done elsewhere. Text keeps the full description.</summary>
public sealed record PlanBlock(PlanBlockKind Kind, string Text, int Minutes, string Title, IReadOnlyList<string> Bullets, IReadOnlyList<ClassStep>? Timed = null);

public sealed record PlanDay(bool IsRest, IReadOnlyList<PlanBlock> Blocks, string? RestText = null);

/// <summary>Days are Monday first.</summary>
public sealed record PlanWeek(int Number, int Book, string BookTitle, int WeekOfBook, int WeeksInBook, string Theme, string Reading,
    string ReadingShort, string DailyCheck, IReadOnlyList<PlanDay> Days);

public sealed record PracticePlan(IReadOnlyList<PlanWeek> Weeks, IReadOnlyList<PlanDay> ForeverWeek, string MonthlyRitual,
    IReadOnlyList<string> MonthlyRitualBullets, string GuideText)
{
    /// <summary>The built-in plan. Decoding it is covered by the tests, so a failure here is a build mistake.</summary>
    public static PracticePlan Shared { get; } = Load();

    static PracticePlan Load()
    {
        using var stream = typeof(PracticePlan).Assembly.GetManifestResourceStream("plan.json")!;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, RespectRequiredConstructorParameters = true };
        return JsonSerializer.Deserialize<PracticePlan>(stream, options)!;
    }
}
