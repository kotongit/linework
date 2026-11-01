using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Linework.App.Library;
using Linework.App.Theme;
using Linework.Core.Copy;
using Linework.Core.Plan;
using Linework.Core.Session;

namespace Linework.App.Views;

/// <summary>One day's practice: what the plan says, with a checkmark per block. Any lesson can be ticked at any time.
/// bordered is off where the card already sits in a frame of its own. glance is Today's list: just the blocks, for reading at a glance.</summary>
public sealed class DayPlanCard : StoreView
{
    readonly LineworkContext _context;
    readonly LibraryStore _library;
    readonly PlanDayRef _ref;
    readonly bool _bordered, _glance;
    readonly HashSet<PlanBlockKind> _expanded = [];

    public DayPlanCard(LineworkContext context, LibraryStore library, PlanDayRef r, bool bordered = true, bool glance = false) : base(library)
    {
        _context = context;
        _library = library;
        _ref = r;
        _bordered = bordered;
        _glance = glance;
        Refresh();
    }

    protected override UIElement Render()
    {
        var schedule = _library.Schedule;
        var day = schedule.DayFor(_ref);
        var panel = Ui.Column(20, Header(schedule));
        if (day.IsRest)
        {
            if (!_glance) Ui.Add(panel, Ui.Rule(), 20);
            Ui.Add(panel, Ui.Text(LineworkCopy.RestDay, 20, Ui.Medium), 20);
        }
        else
        {
            Ui.Add(panel, Rows(day), 20);
        }
        return new Border
        {
            Padding = new Thickness(_glance ? 0 : _bordered ? 28 : 8),
            CornerRadius = new CornerRadius(16),
            BorderBrush = Tokens.Hairline,
            BorderThickness = new Thickness(_bordered && !_glance ? 1 : 0),
            Child = panel,
        };
    }

    FrameworkElement Header(PlanSchedule schedule)
    {
        if (_glance) return Ui.Text(schedule.TitleFor(_ref), 14, null, Tokens.Muted);
        var panel = Ui.Column(6, Ui.Text(schedule.TitleFor(_ref), 26, Ui.Medium).Margin(0, 0, 0, 4));
        if (_ref is PlanRef p)
        {
            var entry = schedule.Sequence[p.SequenceIndex];
            var week = schedule.Plan.Weeks[entry.WeekIndex];
            var line = Ui.Row(10, Ui.Text($"Book {week.Book:00} · Week {week.WeekOfBook} of {week.WeeksInBook} · {week.Theme}", 14, Ui.SemiBold, wrap: true));
            if (entry.IsRepeat) Ui.Add(line, Ui.Pill("Repeat"), 10);
            Ui.Add(panel, line, 6);
            Ui.Add(panel, Ui.Text(week.BookTitle, 14, null, Tokens.Muted), 6);
            Ui.Add(panel, Ui.Text($"Read: {week.ReadingShort}", 14, null, Tokens.Muted), 6);
        }
        else
        {
            Ui.Add(panel, Ui.Text("Your focus is your current weakest skill", 14, Ui.SemiBold), 6);
        }
        return panel;
    }

    FrameworkElement Rows(PlanDay day)
    {
        var panel = Ui.Column(0);
        for (var i = 0; i < day.Blocks.Count; i++)
        {
            var block = day.Blocks[i];
            if (!_glance || i > 0) panel.Children.Add(Ui.Rule());
            var row = Row(block);
            row.Margin = new Thickness(0, _glance && i == 0 ? 0 : 18, 0, i == day.Blocks.Count - 1 ? 0 : 18);
            panel.Children.Add(row);
        }
        return panel;
    }

    /// <summary>A block as a title and bullets. More reveals the plan's full text in place.</summary>
    FrameworkElement Row(PlanBlock block)
    {
        var isChecked = _library.IsTicked(_ref, block.Kind);
        var dock = new DockPanel();
        var circle = new Grid { Width = 26, Height = 26 };
        if (isChecked)
        {
            circle.Children.Add(new Ellipse { Fill = Tokens.Teal });
            circle.Children.Add(Ui.Icon(Ui.CheckIcon, 18, Brushes.White, true, 2.5));
        }
        else
        {
            circle.Children.Add(new Ellipse { Stroke = Tokens.Ink, StrokeThickness = 1, Fill = Brushes.Transparent });
        }
        var toggle = Ui.Plain(circle, () => Toggle(block), isChecked ? "Untick" : "Tick").Align(HorizontalAlignment.Left, VerticalAlignment.Top).Margin(0, 0, 16);
        DockPanel.SetDock(toggle, Dock.Left);
        dock.Children.Add(toggle);

        var body = Ui.Column(6);
        Ui.Add(body, _glance
            ? Ui.Text(block.Kind == PlanBlockKind.Warmup ? block.Kind.Label() : block.Title, 20, Ui.Medium)
            : Ui.Text($"{block.Kind.Label()} · {block.Minutes} min · {block.Title}", 15, Ui.SemiBold, wrap: true).Margin(0, 3), 6);
        foreach (var bullet in block.Bullets)
            Ui.Add(body, Ui.Row(8, Ui.Text("–", 14, null, Tokens.Muted), Ui.Text(bullet, 14, null, isChecked ? Tokens.Muted : Tokens.Ink)), 6);
        if (_expanded.Contains(block.Kind))
            Ui.Add(body, Ui.SelectableText(block.Text, 14, Tokens.Muted).Margin(0, 4), 6);
        // Long class sessions don't fit beside More in a popup, so these wrap.
        var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        if (block.Timed is { } timed)
        {
            var hasImages = _library.VisiblePhotos.Count > 0;
            var start = Ui.Capsule("Start " + string.Join(" + ", timed.Select(s => $"{s.Count} × {DurationFormat.Short(s.Seconds)}")),
                () => PlanStarter.Start(block, _ref, _library, _context), 30);
            start.IsEnabled = hasImages;
            start.ToolTip = hasImages ? "Timed session; finishing it ticks this" : "Add photos to your library first";
            ToolTipService.SetShowOnDisabled(start, true);
            start.Margin = new Thickness(0, 0, 16, 4);
            actions.Children.Add(start);
        }
        if (!_glance)
        {
            var more = Ui.Link(_expanded.Contains(block.Kind) ? "Less" : "More", () =>
            {
                if (!_expanded.Remove(block.Kind)) _expanded.Add(block.Kind);
                Invalidate();
            }, 13, Ui.Medium, Tokens.Muted, true);
            more.MinHeight = 30;
            more.VerticalContentAlignment = VerticalAlignment.Center;
            actions.Children.Add(more);
        }
        if (actions.Children.Count > 0) Ui.Add(body, actions, 6);
        dock.Children.Add(body);
        return dock;
    }

    public void Toggle(PlanBlock block)
    {
        if (!_library.ToggleTick(_ref, block.Kind)) return;
        if (PlanProgress.CelebrationFor(_ref, _library.TickKeys, _library.Schedule) is { } celebration)
            _context.Session.Celebrate(celebration, _ref == _library.Schedule.TodayRef(_context.Session.Today));
    }
}

/// <summary>A day's card in a popup, for the Path and Calendar. It belongs to the view, not the node, so it outlives a redraw.</summary>
public sealed class DayPopup
{
    readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, PopupAnimation = PopupAnimation.None };

    public bool IsOpen => _popup.IsOpen;
    public event Action? Closed;

    public DayPopup() => _popup.Closed += (_, _) => Closed?.Invoke();

    public void Show(FrameworkElement target, UIElement content, double height = 460, bool scroll = true)
    {
        _popup.PlacementTarget = target;
        _popup.Child = new Border
        {
            Background = Tokens.Ground,
            BorderBrush = Tokens.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Width = scroll ? 520 : double.NaN,
            Height = scroll ? height : double.NaN,
            Padding = new Thickness(scroll ? 4 : 20),
            Child = scroll ? new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12), Content = content } : content,
        };
        _popup.IsOpen = true;
    }
}
