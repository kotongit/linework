using System.Windows;
using System.Windows.Controls;
using Linework.App.Library;
using Linework.App.Theme;

namespace Linework.App.Views;

public sealed class TodayView : StoreView
{
    readonly LineworkContext _context;
    readonly LibraryStore _library;
    readonly ScrollViewer _scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    object? _shownRef;

    public TodayView(LineworkContext context, LibraryStore library) : base(library, context.Session)
    {
        _context = context;
        _library = library;
        Refresh();
    }

    protected override UIElement Render()
    {
        var r = _library.Schedule.TodayRef(_context.Session.Today);
        // A new day starts with every row collapsed; the same day keeps its card, which redraws itself.
        if (!Equals(r, _shownRef) || _library.Schedule.Repeats.Count != _shownRepeats)
        {
            _shownRef = r;
            _shownRepeats = _library.Schedule.Repeats.Count;
            var grid = new Grid { Margin = new Thickness(72, 40, 72, 40), MaxWidth = 956, HorizontalAlignment = HorizontalAlignment.Left };
            grid.Children.Add(new DayPlanCard(_context, _library, r, glance: true));
            var options = Ui.Circle(Ui.Icon(Ui.EllipsisIcon, 16), () => { }, "Plan options", 30).Align(HorizontalAlignment.Right, VerticalAlignment.Top);
            options.Click += (_, _) => ShowOptions(options);
            grid.Children.Add(options);
            _scroller.Content = grid;
        }
        return _scroller;
    }

    int _shownRepeats = -1;

    void ShowOptions(Button button)
    {
        var schedule = _library.Schedule;
        var today = _context.Session.Today;
        MenuItem Item(string title, Action click, bool enabled = true)
        {
            var item = new MenuItem { Header = title, IsEnabled = enabled };
            item.Click += (_, _) => click();
            return item;
        }
        var menu = new ContextMenu { PlacementTarget = button };
        menu.Items.Add(Item("Repeat This Week…", () => AskRepeat(1), schedule.CanAddRepeat(1, today)));
        menu.Items.Add(Item("Repeat This Week and Last…", () => AskRepeat(2), schedule.CanAddRepeat(2, today)));
        menu.Items.Add(Item("Undo Last Repeat", _library.UndoLastRepeat, schedule.CanUndoLastRepeat(today)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("How This Plan Works", () => new PlanGuideWindow(_context.Settings) { Owner = Window.GetWindow(this) }.ShowDialog()));
        menu.IsOpen = true;
    }

    void AskRepeat(int weeks)
    {
        var total = _library.Schedule.Sequence.Count + weeks;
        var title = weeks == 2 ? "Repeat this week and last week?" : "Repeat this week?";
        // They run again after the current week; nothing before next Monday moves.
        var text = $"{(weeks == 2 ? "They run" : "It runs")} again from next Monday, and your plan will now run for {total} weeks.";
        if (AlertWindow.Ask(title, text, "Repeat", "Cancel") == 0) _library.AddRepeat(weeks, _context.Session.Today);
    }
}

/// <summary>"How this plan works": the rules and warm-up table from the plan.</summary>
public sealed class PlanGuideWindow : GlassWindow
{
    public PlanGuideWindow(Settings settings) : base(settings)
    {
        Title = "How This Plan Works";
        Width = 640;
        Height = 680;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        KeyDown += (_, e) =>
        {
            if (e.Key is System.Windows.Input.Key.Escape or System.Windows.Input.Key.Enter) Close();
        };
        Content = Body(Close);
    }

    public static FrameworkElement Body(Action done)
    {
        var dock = new DockPanel();
        var footer = Ui.Column(0, Ui.Rule(), Ui.Capsule("Done", done, 30, true).Align(HorizontalAlignment.Right).Margin(16, 16, 16, 16));
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Focusable = false,
            Content = Ui.SelectableText(Linework.Core.Plan.PracticePlan.Shared.GuideText).Margin(32, 32, 32, 32),
        });
        return dock;
    }
}
