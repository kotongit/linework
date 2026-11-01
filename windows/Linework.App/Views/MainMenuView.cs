using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Linework.App.Library;
using Linework.App.Theme;
using Linework.Core.Copy;
using Linework.Core.Plan;

namespace Linework.App.Views;

/// <summary>The home screen: a live clock, a greeting, section links and today's facts.</summary>
public sealed class MainMenuView : StoreView
{
    readonly LineworkContext _context;
    readonly LibraryStore _library;
    readonly Action _openSettings;
    readonly Action<PlanDayRef> _startNext;
    readonly DispatcherTimer _minute = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly Func<DateTime> _now;

    public MainMenuView(LineworkContext context, LibraryStore library, Action openSettings, Action<PlanDayRef> startNext, Func<DateTime>? now = null)
        : base(library, context.Session, context.Settings)
    {
        _context = context;
        _library = library;
        _openSettings = openSettings;
        _startNext = startNext;
        _now = now ?? (() => DateTime.Now);
        _minute.Tick += (_, _) => Invalidate();
        Loaded += (_, _) => _minute.Start();
        Unloaded += (_, _) => _minute.Stop();
        Refresh();
    }

    protected override UIElement Render()
    {
        var grid = new Grid { Margin = new Thickness(72, 22, 72, 56) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = Header();
        var hero = Hero().Align(HorizontalAlignment.Stretch, VerticalAlignment.Center);
        var footer = Footer().Align(HorizontalAlignment.Stretch, VerticalAlignment.Bottom);
        Grid.SetRow(hero, 1);
        Grid.SetRow(footer, 2);
        grid.Children.Add(header);
        grid.Children.Add(hero);
        grid.Children.Add(footer);
        return grid;
    }

    FrameworkElement Header()
    {
        var dock = new DockPanel();
        var left = Ui.Row(0, Ui.Asterisk(22, Tokens.Muted), Ui.Text("Linework", 14, Ui.SemiBold).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Margin(10), Ui.Pill("Drawing companion").Margin(20));
        DockPanel.SetDock(left, Dock.Left);
        dock.Children.Add(left);
        if (FeatureFlags.Plan)
        {
            var right = Ui.Row(12, Ui.Capsule("Practice", () => Open(AppSection.Today)),
                Ui.Circle(Ui.Icon(Ui.ArrowDownRightIcon, 20, stroke: true), () => _startNext(_library.Schedule.TodayRef(_context.Session.Today)), "Start today's session"));
            right.HorizontalAlignment = HorizontalAlignment.Right;
            dock.Children.Add(right);
        }
        return dock;
    }

    void Open(AppSection section) => _context.Session.SetRoute(new AppRoute(section));

    FrameworkElement Hero()
    {
        var name = _context.Settings.Current.Name;
        var links = Ui.Row(28);
        foreach (var section in AppSections.Visible)
            Ui.Add(links, Ui.Plain(new Border
            {
                MinHeight = 44,
                Background = System.Windows.Media.Brushes.Transparent,
                Child = Ui.Text(section.Title(), 15, Ui.Medium).Align(HorizontalAlignment.Left, VerticalAlignment.Center),
            }, () => Open(section)), 28);
        return Ui.Column(0,
            new HeroClock(_context, () => TimeOnly.FromDateTime(_now())),
            Ui.Text(LineworkCopy.Greeting(_now().Hour, name), 26, Ui.Medium, maxWidth: 760).Align(HorizontalAlignment.Left).Margin(0, 28),
            Ui.Text(LineworkCopy.UnderGreeting, 15, null, Tokens.Muted, 760).Align(HorizontalAlignment.Left).Margin(0, 8),
            links.Margin(0, 28));
    }

    FrameworkElement Footer()
    {
        var grid = new Grid();
        var columns = new List<FrameworkElement>();
        var name = _context.Settings.Current.Name;
        columns.Add(Column("Artist", name.Length == 0
            ? Ui.Link("Add your name", _openSettings, 14, Ui.SemiBold, underline: true).Align(HorizontalAlignment.Left)
            : Value(name)));
        columns.Add(Column("Date", Value(_now().ToString("dddd, MMMM d, yyyy", CultureInfo.GetCultureInfo("en-US")))));
        columns.Add(Column("Local time zone", Value(ZoneName())));
        if (FeatureFlags.Plan)
            columns.Add(Column("Today's practice", Value(_library.Schedule.PracticeLine(_library.Schedule.TodayRef(_context.Session.Today)))));
        for (var i = 0; i < columns.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(columns[i], i);
            columns[i].Margin = new Thickness(0, 0, i < columns.Count - 1 ? 48 : 0, 0);
            grid.Children.Add(columns[i]);
        }
        return grid;
    }

    static string ZoneName()
    {
        var id = TimeZoneInfo.Local.Id;
        return (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) ? iana : id).Replace('_', ' ');
    }

    static TextBlock Value(string text)
    {
        var t = Ui.Text(text, 14, Ui.SemiBold, wrap: true);
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.MaxHeight = 40;
        return t;
    }

    static StackPanel Column(string label, FrameworkElement value) => Ui.Column(4, Ui.Text(label, 12, null, Tokens.Muted), value);
}
