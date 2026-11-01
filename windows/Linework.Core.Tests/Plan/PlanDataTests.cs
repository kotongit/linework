namespace Linework.Core.Tests;

public class PlanDataTests
{
    [Fact]
    public void PlanDataMatchesSwiftSource()
    {
        var swift = File.ReadAllText(Path.Combine(Repo.Root, "Sources/LineworkCore/PracticePlanData.swift")).ReplaceLineEndings("\n");
        const string open = "##\"\"\"\n", close = "\n\"\"\"##";
        var start = swift.IndexOf(open, StringComparison.Ordinal) + open.Length;
        var end = swift.LastIndexOf(close, StringComparison.Ordinal);
        using var stream = typeof(LineworkVersion).Assembly.GetManifestResourceStream("plan.json")!;
        Assert.Equal(swift[start..end], new StreamReader(stream).ReadToEnd());
    }

    [Fact]
    public void VersionComesFromBuildProps() => Assert.Equal("2.0", LineworkVersion.Current);
}
