using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Linework.App.Library;
using Linework.App.Theme;
using Linework.Core.Copy;
using Linework.Core.Plan;
using Linework.Core.Session;

namespace Linework.App.Views;

public sealed class CalendarHeatmapView : StoreView
{
    static readonly uint[] LightRamp = [0xEBEFF0, 0xB2E5E0, 0x5FC9BE, 0x20A396, 0x0B6E65];
    static readonly uint[] DarkRamp = [0x1E2526, 0x0F4A45, 0x16756C, 0x22A99B, 0x4FE3D2];
    const double Cell = 13, Gap = 3;
    static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    readonly LineworkContext _context;
    readonly LibraryStore _library;
    readonly DayPopup _popup = new();
    Border? _open;

    sealed class DaySummary
    {
        public int Seconds, Sessions, Ticks;
    }

    public CalendarHeatmapView(LineworkContext context, LibraryStore library) : base(library, context.Session)
    {
        _context = context;
        _library = library;
        _popup.Closed += () =>
        {
            if (_open != null) _open.BorderThickness = new Thickness(_open.Tag is 0 ? 1 : 0);
            if (_open != null) _open.BorderBrush = _open.Tag is 0 ? Tokens.Hairline : null;
            _open = null;
            Invalidate();
        };
        Refresh();
    }

    protected override bool ShouldRender() => !_popup.IsOpen;

    protected override UIElement Render()
    {
        var stats = DaySummaries();
        var grid = PracticeCalendar.Grid(_context.Session.Today);
        var columns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 8, 4) };
        for (var w = 0; w < grid.Count; w++)
        {
            var column = new StackPanel { Width = Cell, Margin = new Thickness(0, 0, w < grid.Count - 1 ? Gap : 0, 0) };
            var month = new Canvas { Height = 14, Margin = new Thickness(0, 0, 0, Gap) };
            month.Children.Add(Ui.Text(MonthLabel(grid, w), 11, null, Tokens.Muted));
            column.Children.Add(month);
            for (var d = 0; d < 7; d++)
            {
                var square = Square(grid[w][d], stats);
                square.Margin = new Thickness(0, 0, 0, d < 6 ? Gap : 0);
                column.Children.Add(square);
            }
            columns.Children.Add(column);
        }
        var scroller = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = columns, Focusable = false };
        scroller.Loaded += (_, _) => scroller.ScrollToRightEnd();
        var labels = new StackPanel { Margin = new Thickness(0, 4, Gap, 4) };
        labels.Children.Add(new Border { Width = 26, Height = 14 + Gap });
        foreach (var name in new[] { "Mon", "", "Wed", "", "Fri", "", "" })
        {
            var t = Ui.Text(name, 11, null, Tokens.Muted);
            t.TextAlignment = TextAlignment.Right;
            labels.Children.Add(new Border { Width = 26, Height = Cell + Gap, Child = t });
        }
        var row = new DockPanel();
        DockPanel.SetDock(labels, Dock.Left);
        row.Children.Add(labels);
        row.Children.Add(scroller);
        var legend = Ui.Row(Gap, Ui.Text("Less", 12, null, Tokens.Muted).Margin(0, 0, 5));
        for (var level = 0; level < 5; level++) Ui.Add(legend, Tile(level), Gap);
        Ui.Add(legend, Ui.Text("More", 12, null, Tokens.Muted).Margin(5), Gap);
        var panel = Ui.Column(24,
            Ui.Capsule("Today", () => _context.Session.SetRoute(new AppRoute(AppSection.Today)), 30).Align(HorizontalAlignment.Left),
            row,
            legend);
        panel.Margin = new Thickness(72, 0, 72, 0);
        panel.VerticalAlignment = VerticalAlignment.Center;
        panel.MaxWidth = 1100;
        panel.HorizontalAlignment = HorizontalAlignment.Left;
        return panel;
    }

    FrameworkElement Square(DateOnly? day, Dictionary<DateOnly, DaySummary> stats)
    {
        if (day is not { } date) return new Border { Width = Cell, Height = Cell };
        var summary = stats.GetValueOrDefault(date) ?? new DaySummary();
        var tile = Tile(PracticeCalendar.Level(summary.Seconds));
        var r = _library.Schedule.RefOn(date);
        Button? button = null;
        button = Ui.Plain(tile, () =>
        {
            // Outlined while its card is open.
            _open = tile;
            tile.BorderBrush = Tokens.Ink;
            tile.BorderThickness = new Thickness(2);
            if (r != null) _popup.Show(button!, new DayPlanCard(_context, _library, r, false));
            else _popup.Show(button!, Ui.Text(LineworkCopy.NoLessonThatDay, 14), scroll: false);
        }, Tooltip(date, summary));
        return button;
    }

    /// <summary>Level 0 is outlined so an empty day still reads on light glass.</summary>
    static Border Tile(int level)
    {
        var hex = (Tokens.IsDark ? DarkRamp : LightRamp)[Math.Min(Math.Max(level, 0), 4)];
        return new Border
        {
            Width = Cell,
            Height = Cell,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex)),
            BorderBrush = level == 0 ? Tokens.Hairline : null,
            BorderThickness = new Thickness(level == 0 ? 1 : 0),
            Tag = level,
        };
    }

    static string Tooltip(DateOnly day, DaySummary summary)
    {
        var date = day.ToString("MMM d", English);
        var parts = new List<string>();
        if (summary.Sessions > 0) parts.Add($"{summary.Sessions} session{(summary.Sessions == 1 ? "" : "s")}");
        if (summary.Seconds > 0) parts.Add(DurationFormat.Total(summary.Seconds));
        if (summary.Ticks > 0) parts.Add($"{summary.Ticks} {(summary.Ticks == 1 ? "check" : "checks")}");
        return parts.Count == 0 ? $"{date}: No practice" : $"{date}: {string.Join(", ", parts)}";
    }

    /// <summary>What each day adds up to: timed sessions and ticked blocks, without counting the same work twice.</summary>
    Dictionary<DateOnly, DaySummary> DaySummaries()
    {
        var result = new Dictionary<DateOnly, DaySummary>();
        var sessions = _library.Sessions.Select(s => (DateOnly.FromDateTime(s.StartedAt.ToLocalTime().DateTime), s.DrawingSeconds));
        foreach (var (day, stat) in PracticeCalendar.DailyStats(sessions))
            result[day] = new DaySummary { Seconds = stat.Seconds, Sessions = stat.Sessions };
        foreach (var (key, ticked) in PlanProgress.TickSummary(_library.Ticks, _library.Schedule))
        {
            if (PlanDayKey.Date(key) is not { } day) continue;
            if (!result.TryGetValue(day, out var summary)) result[day] = summary = new DaySummary();
            summary.Ticks = ticked.Count;
            summary.Seconds = PlanProgress.HeatmapSeconds(summary.Sessions > 0 ? summary.Seconds : 0, ticked.Minutes);
        }
        return result;
    }

    /// <summary>Month name above the first column that contains the 1st of a month (and the first column).</summary>
    static string MonthLabel(IReadOnlyList<IReadOnlyList<DateOnly?>> grid, int w)
    {
        var days = grid[w].OfType<DateOnly>().ToList();
        if (days.Count == 0) return "";
        var first = days.FirstOrDefault(d => d.Day == 1);
        if (w != 0 && first == default) return "";
        return (first == default ? days[0] : first).ToString("MMM", English);
    }
}
