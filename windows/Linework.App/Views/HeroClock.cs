using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Linework.App.Theme;
using Linework.Core;

namespace Linework.App.Views;

/// <summary>The big clock: HH:MM, seconds and AM/PM. Follows the 24-hour and Show seconds settings.</summary>
public sealed class HeroClock : UserControl
{
    const double Tracking = -9;

    readonly LineworkContext _context;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Func<TimeOnly> _now;

    public HeroClock(LineworkContext context, Func<TimeOnly>? now = null)
    {
        _context = context;
        _now = now ?? (() => TimeOnly.FromDateTime(DateTime.Now));
        _timer.Tick += (_, _) => Draw();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
        Draw();
    }

    void Draw()
    {
        var settings = _context.Settings.Current;
        var parts = ClockFormat.Parts(_now(), settings.Clock24h);
        // WPF has no tracking setting, so each glyph sets its own overlap.
        var time = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in parts.Time)
            time.Children.Add(Ui.Text(c.ToString(), 216, Ui.Medium, tabular: true).Margin(Tracking / 2, 0, Tracking / 2));
        System.Windows.Automation.AutomationProperties.SetName(time, parts.Time + (parts.Meridiem != null ? " " + parts.Meridiem : ""));
        var side = Ui.Column(4).Margin(8, 22);
        if (settings.ClockSeconds) Ui.Add(side, Ui.Text(parts.Seconds, 52, null, Tokens.Muted, tabular: true), 4);
        if (parts.Meridiem != null) Ui.Add(side, Ui.Text(parts.Meridiem, 14, Ui.SemiBold, Tokens.Muted), 4);
        Content = new Viewbox
        {
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = Ui.Row(0, time, side),
        };
    }
}
