using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Linework.App.Theme;
using Linework.Core;
using Linework.Core.Copy;

namespace Linework.App.Views;

public sealed class SettingsWindow : GlassWindow
{
    public SettingsWindow(LineworkContext context) : base(context.Settings)
    {
        Title = "Settings";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Content = Body(context);
    }

    public static FrameworkElement Body(LineworkContext context)
    {
        var settings = context.Settings;
        var (field, box) = Ui.UnderlinedField(20, LineworkCopy.MaxNameLength, "Your name");
        box.Text = settings.Current.Name;
        box.TextChanged += (_, _) =>
        {
            var normalized = LineworkCopy.NormalizedName(box.Text);
            settings.Update(v => v with { Name = normalized, Welcomed = true });
        };
        var panel = Ui.Column(20,
            Ui.Column(6, Ui.Text("Your name", 12, Ui.Medium, Tokens.Muted), field),
            Ui.Toggle("24-hour clock", settings.Current.Clock24h, on => settings.Update(v => v with { Clock24h = on })),
            Ui.Toggle("Show seconds", settings.Current.ClockSeconds, on => settings.Update(v => v with { ClockSeconds = on })),
            Ui.Toggle("Notes from s.e.a.", settings.Current.Notes, on => settings.Update(v => v with { Notes = on })));
        // The Transparency setting applies only where Mica is available.
        if (MicaAvailable)
        {
            double Clamped() => Math.Min(Math.Max(settings.Current.Transparency, Glass.MinTransparency), Glass.MaxTransparency);
            var percent = Ui.Text("", 14, null, Tokens.Muted, tabular: true);
            void Show() => percent.Text = ((int)Math.Round(Clamped() * 100, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + "%";
            Show();
            var header = new DockPanel();
            DockPanel.SetDock(percent, Dock.Right);
            header.Children.Add(percent);
            header.Children.Add(Ui.Text("Transparency"));
            var slider = new Slider { Minimum = Glass.MinTransparency, Maximum = Glass.MaxTransparency, Value = Clamped() };
            slider.ValueChanged += (_, e) =>
            {
                settings.Update(v => v with { Transparency = e.NewValue });
                Show();
            };
            Ui.Add(panel, Ui.Column(6, header, slider), 20);
        }
        panel.Margin = new Thickness(28);
        return panel;
    }
}
