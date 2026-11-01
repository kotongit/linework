using System.Windows;
using Linework.App.Library;
using Linework.Core.Plan;

namespace Linework.App.Views;

/// <summary>Starting plan blocks as sessions. Every decision about what progress means lives in Core.</summary>
public static class PlanStarter
{
    /// <summary>Starts a timed block as a session in the player window. False when there is nothing to start.</summary>
    public static bool Start(PlanBlock block, PlanDayRef r, LibraryStore library, LineworkContext context)
    {
        var photos = ReadablePhotos.Filter(library, context.App.Host);
        if (SessionController.QueueFor(block, photos, context.Session.Today, Random.Shared) is not { } queue) return false;
        var session = context.Session;
        session.Queue = queue;
        session.PlanBlock = new PlanBlockRef(r, block.Kind);
        session.SoundOn = context.Settings.Current.Sound;
        session.StartGrayscale = context.Settings.Current.SetupGrayscale;
        SessionPlayerWindow.Open(context, library, Application.Current?.MainWindow);
        return true;
    }
}
