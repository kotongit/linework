using System.Reflection;

namespace Linework.Core;

public static class LineworkVersion
{
    /// <summary>The one place it's set is Directory.Build.props.</summary>
    public static string Current { get; } =
        typeof(LineworkVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
}
