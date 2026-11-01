using System.Windows;
using System.Windows.Controls;
using Linework.App.Library;
using Linework.App.Theme;
using Linework.Core.Plan;

namespace Linework.App.Views;

/// <summary>Slim strip at the top of every section: menu mark, section pills, streak and Start Session. 52pt tall.</summary>
public sealed class TopBar : StoreView
{
    readonly LineworkContext _context;
    readonly LibraryStore _library;
    readonly Action _startSession;

    public TopBar(LineworkContext context, LibraryStore library, Action startSession) : base(library, context.Session)
    {
        _context = context;
        _library = library;
        _startSession = startSession;
        Refresh();
    }

    protected override UIElement Render()
    {
        var route = _context.Session.Route;
        var grid = new Grid { Height = 52 };
        var pills = Ui.Row(6).Align(HorizontalAlignment.Center, VerticalAlignment.Center);
        foreach (var item in AppSections.Visible)
            Ui.Add(pills, Ui.Capsule(item.Title(), () => _context.Session.SetRoute(new AppRoute(item)), 30, route.Section == item), 6);
        grid.Children.Add(pills);
        var mark = Ui.Plain(new Border { Width = 30, Height = 30, Background = System.Windows.Media.Brushes.Transparent, Child = Ui.Asterisk(20) },
            () => _context.Session.SetRoute(AppRoute.Menu), "Main menu").Align(HorizontalAlignment.Left, VerticalAlignment.Center).Margin(16);
        grid.Children.Add(mark);
        var right = Ui.Row(0).Align(HorizontalAlignment.Right, VerticalAlignment.Center);
        if (FeatureFlags.Plan)
        {
            var streak = PlanProgress.CurrentStreak(_library.PracticeDays, _context.Session.Today);
            var flame = Ui.Row(4, Ui.Icon(Ui.FlameIcon, 15).Align(HorizontalAlignment.Left, VerticalAlignment.Center),
                Ui.Text(streak.ToString(System.Globalization.CultureInfo.InvariantCulture), 13, Ui.Medium, tabular: true).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
            flame.VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(flame, $"Streak, {streak} days");
            right.Children.Add(flame);
        }
        right.Children.Add(Ui.Capsule("Start Session", _startSession, 30, true).Margin(12, 0, 16));
        grid.Children.Add(right);
        var rule = Ui.Rule().Align(HorizontalAlignment.Stretch, VerticalAlignment.Bottom);
        grid.Children.Add(rule);
        return grid;
    }
}
