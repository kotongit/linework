using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Linework.App.Library;
using Linework.App.Theme;
using Linework.Core.Plan;

namespace Linework.App.Views;

/// <summary>The whole plan as one straight line of day circles, grouped by book.</summary>
public sealed class PathView : StoreView
{
    const double RowHeight = 52;
    static readonly string[] Initials = ["M", "T", "W", "T", "F", "S", "S"];

    readonly LineworkContext _context;
    readonly LibraryStore _library;
    readonly ScrollViewer _scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    readonly DayPopup _popup = new();
    FrameworkElement? _anchor;
    bool _scrolled;

    public PathView(LineworkContext context, LibraryStore library) : base(library, context.Session)
    {
        _context = context;
        _library = library;
        _popup.Closed += Invalidate;
        Loaded += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, ScrollToToday);
        Refresh();
    }

    /// <summary>A redraw would take the node a popup hangs from, so it waits.</summary>
    protected override bool ShouldRender() => !_popup.IsOpen;

    /// <summary>The week containing today, the first week before the start, the forever node after the end.</summary>
    void ScrollToToday()
    {
        if (_scrolled || _anchor == null || _scroller.Content is not Visual content || !_anchor.IsDescendantOf(content)) return;
        _scrolled = true;
        var y = _anchor.TranslatePoint(new Point(0, 0), (UIElement)content).Y;
        _scroller.ScrollToVerticalOffset(Math.Max(y + _anchor.ActualHeight / 2 - _scroller.ActualHeight / 2, 0));
    }

    protected override UIElement Render()
    {
        var schedule = _library.Schedule;
        var ticks = (IReadOnlySet<string>)_library.TickKeys;
        var today = _context.Session.Today;
        var todayRef = schedule.TodayRef(today);
        var panel = new StackPanel { Margin = new Thickness(0, 24, 0, 24) };
        for (var i = 0; i < schedule.Sequence.Count; i++)
        {
            var week = schedule.Plan.Weeks[schedule.Sequence[i].WeekIndex];
            var startsBook = i == 0 || schedule.Plan.Weeks[schedule.Sequence[i - 1].WeekIndex].Book != week.Book;
            var section = WeekSection(schedule, i, week, startsBook, ticks, today, todayRef);
            if (todayRef is PlanRef p && p.SequenceIndex == i) _anchor = section;
            panel.Children.Add(section);
        }
        var forever = ForeverNode(new ForeverRef(PlanDayKey.Key(schedule.DateOf(schedule.Sequence.Count, 0))));
        if (todayRef is ForeverRef) _anchor = forever;
        panel.Children.Add(forever);
        _scroller.Content = panel;
        return _scroller;
    }

    FrameworkElement WeekSection(PlanSchedule schedule, int index, PlanWeek week, bool startsBook, IReadOnlySet<string> ticks, DateOnly today, PlanDayRef todayRef)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Center };
        if (startsBook)
        {
            var title = Ui.Text(week.BookTitle, 26, Ui.Medium, maxWidth: 560);
            title.TextAlignment = TextAlignment.Center;
            title.HorizontalAlignment = HorizontalAlignment.Center;
            var head = Ui.Column(12, Ui.Pill($"Book {week.Book:00}").Align(HorizontalAlignment.Center, VerticalAlignment.Top), title).Margin(0, 36, 0, 20);
            head.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(head);
        }
        var line = Ui.Row(8, Ui.Text($"Week {week.WeekOfBook} · {week.Theme}", 14, null, Tokens.Muted).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
        if (schedule.Sequence[index].IsRepeat) Ui.Add(line, Ui.Pill("Repeat"), 8);
        line.HorizontalAlignment = HorizontalAlignment.Center;
        line.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(line);
        var days = new Grid { Height = RowHeight * 7, Width = 60, HorizontalAlignment = HorizontalAlignment.Center };
        days.Children.Add(new Rectangle { Fill = Tokens.Muted, Width = 1 });
        var nodes = new StackPanel();
        for (var weekday = 0; weekday < 7; weekday++)
        {
            var r = new PlanRef(index, weekday);
            var node = DayNode(r, PlanProgress.Status(r, ticks, schedule), r == todayRef, schedule.DateOf(r) > today, weekday, schedule.TitleFor(r));
            nodes.Children.Add(new Grid { Height = RowHeight, Children = { node } });
        }
        days.Children.Add(nodes);
        panel.Children.Add(days);
        return panel;
    }

    FrameworkElement DayNode(PlanRef r, DayStatus status, bool isToday, bool isFuture, int weekday, string help)
    {
        var size = status == DayStatus.Rest && !isToday ? 22.0 : isToday ? 44 : 32;
        // Future days are drawn in hairline rather than faded, so their letters stay readable on glass.
        Brush outline = isFuture ? Tokens.Hairline : Tokens.Muted;
        var grid = new Grid { Width = size, Height = size };
        // Hides the line at any transparency.
        grid.Children.Add(new Ellipse { Fill = Tokens.Ground });
        UIElement Letter(FontWeight weight, Brush color) => Ui.Text(Initials[weekday], 13, weight, color).Align(HorizontalAlignment.Center, VerticalAlignment.Center);
        switch (status)
        {
            case DayStatus.Rest:
                grid.Children.Add(new Ellipse { Stroke = isToday ? Tokens.Teal : outline, StrokeThickness = isToday ? 2 : 1 });
                grid.Children.Add(Ui.Icon(Ui.MoonIcon, 12, Tokens.Muted));
                break;
            case DayStatus.Complete:
                grid.Children.Add(new Ellipse { Fill = Tokens.Teal });
                grid.Children.Add(Ui.Icon(Ui.CheckIcon, isToday ? 26 : 19, Brushes.White, true, 2.5));
                break;
            case DayStatus.Partial:
                grid.Children.Add(new Ellipse { Stroke = Tokens.Teal, StrokeThickness = 2 });
                grid.Children.Add(Letter(Ui.SemiBold, Tokens.Ink));
                break;
            default:
                grid.Children.Add(new Ellipse { Stroke = isToday ? Tokens.Teal : outline, StrokeThickness = isToday ? 2 : 1 });
                grid.Children.Add(Letter(isToday ? Ui.SemiBold : FontWeights.Normal, isToday ? Tokens.Ink : Tokens.Muted));
                break;
        }
        if (isToday) grid.Effect = new DropShadowEffect { Color = Tokens.Teal.Color, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.7 };
        Button? button = null;
        button = Ui.Plain(grid, () => _popup.Show(button!, new DayPlanCard(_context, _library, r, false)), help).Align(HorizontalAlignment.Center, VerticalAlignment.Center);
        return button;
    }

    FrameworkElement ForeverNode(PlanDayRef r)
    {
        Button? button = null;
        var body = Ui.Column(8, Ui.Asterisk(28).Align(HorizontalAlignment.Center, VerticalAlignment.Top), Ui.Text("Forever week", 26, Ui.Medium));
        body.Margin = new Thickness(0, 18, 0, 18);
        button = Ui.Plain(body, () => _popup.Show(button!, new DayPlanCard(_context, _library, r, false), 400)).Align(HorizontalAlignment.Center, VerticalAlignment.Top);
        return button;
    }
}
