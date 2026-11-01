namespace Linework.App.Library;

public enum AppSection { Today, Path, Calendar, Library, Progress, Freehand }

public static class AppSections
{
    public static string Title(this AppSection section) => section.ToString();

    public static IReadOnlyList<AppSection> Visible => Enum.GetValues<AppSection>().Where(IsVisible).ToList();

    public static bool IsVisible(this AppSection section) => section switch
    {
        AppSection.Library => true,
        AppSection.Progress or AppSection.Freehand => FeatureFlags.BenchmarksAndFreehand,
        _ => FeatureFlags.Plan,
    };
}

/// <summary>Where the main window is: the main menu (null section) or a section.</summary>
public sealed record AppRoute(AppSection? Section)
{
    public static readonly AppRoute Menu = new((AppSection?)null);
}
