namespace Linework.Core.Tests;

public static class Repo
{
    public static string Root { get; } = Find();

    static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Package.swift"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }

    public static string Fixture(params string[] parts) =>
        Path.Combine([Root, "shared", "fixtures", .. parts]);
}
