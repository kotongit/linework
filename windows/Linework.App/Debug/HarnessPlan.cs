#if DEBUG
using System.Windows;
using Linework.App.Library;
using Linework.App.Platform;
using Linework.App.Views;
using Linework.Core.Plan;

namespace Linework.App.Debugging;

public static partial class Harness
{
    static async Task PlanStages(LineworkContext context, LibraryStore library)
    {
        SoundCue.Muted = true;
        var today = context.Session.Today;
        var r = library.Schedule.TodayRef(today);
        // A lesson with blocks, ticked through the real card, ends in a celebration.
        PlanDayRef lesson = library.Schedule.DayFor(r).IsRest ? new PlanRef(0, 0) : r;
        var card = new DayPlanCard(context, library, lesson);
        foreach (var block in library.Schedule.DayFor(lesson).Blocks) card.Toggle(block);
        Check("Ticking every block completes the day", PlanProgress.Status(lesson, library.TickKeys, library.Schedule) == DayStatus.Complete);
        Check("Finishing a day celebrates", context.Session.PendingCelebration != null);
        context.Session.ClearCelebration();
        foreach (var block in library.Schedule.DayFor(new PlanRef(1, 2)).Blocks.Take(1)) library.ToggleTick(new PlanRef(1, 2), block.Kind);

        await Shoot("today", new TodayView(context, library), 1280, 718);
        await Shoot("plan-card", new DayPlanCard(context, library, new PlanRef(1, 1)), 900, 700);
        var path = new PathView(context, library);
        await Shoot("path", path, 1280, 718);
        await Shoot("calendar", new CalendarHeatmapView(context, library), 1280, 718);
        await Shoot("guide", PlanGuideWindow.Body(() => { }), 640, 640);
        await Shoot("rest", new DayPlanCard(context, library, new PlanRef(0, 6)), 700, 300);
        await Shoot("confetti", new CelebrationView(Celebration.Book, true, false, () => { }, 1.2), 1280, 718);
    }
}
#endif
