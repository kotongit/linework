using Linework.Core.Plan;
using Linework.Core.Session;

namespace Linework.App.Library;

/// <summary>The plan block a session ticks when it finishes.</summary>
public sealed record PlanBlockRef(PlanDayRef Ref, PlanBlockKind Kind);

/// <summary>Where the main window is, and hands the configured session from setup to the player.</summary>
public sealed class SessionStore(ILibraryHost host) : ObservableObject
{
    public SessionQueue? Queue { get; set; }
    public bool SoundOn { get; set; } = true;
    public bool StartGrayscale { get; set; }
    /// <summary>The plan block this session ticks when it finishes; null for ordinary sessions.</summary>
    public PlanBlockRef? PlanBlock { get; set; }
    public bool IsSessionActive { get; private set; }
    public AppRoute Route { get; private set; } = AppRoute.Menu;
    public Celebration? PendingCelebration { get; private set; }
    /// <summary>False when the celebrated tick belongs to an earlier day.</summary>
    public bool PendingCelebrationIsToday { get; private set; } = true;
    /// <summary>Today, for plan views. Kept current so an app left open overnight moves on to the new day.</summary>
    public DateOnly Today { get; private set; } = host.Today;

    public event Action? ImportRequested;

    public void RequestImport() => ImportRequested?.Invoke();

    public void SetRoute(AppRoute route)
    {
        Route = route;
        NotifyChanged();
    }

    public void SetSessionActive(bool active)
    {
        IsSessionActive = active;
        NotifyChanged();
    }

    /// <summary>isToday says whether the finished lesson is today's, which sets the wording.</summary>
    public void Celebrate(Celebration celebration, bool isToday)
    {
        PendingCelebrationIsToday = isToday;
        PendingCelebration = celebration;
        NotifyChanged();
    }

    public void ClearCelebration()
    {
        PendingCelebration = null;
        NotifyChanged();
    }

    public void RefreshToday()
    {
        if (host.Today == Today) return;
        Today = host.Today;
        NotifyChanged();
    }
}
