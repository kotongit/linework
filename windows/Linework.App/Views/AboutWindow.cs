using System.Windows;
using Linework.App.Theme;
using Linework.Core;

namespace Linework.App.Views;

public sealed class AboutWindow : GlassWindow
{
    public AboutWindow(Library.Settings settings) : base(settings)
    {
        Title = "About Linework";
        Width = 420;
        Height = 280;
        ResizeMode = ResizeMode.NoResize;
        Content = Body();
    }

    public static FrameworkElement Body()
    {
        var panel = Ui.Column(10,
            Ui.Asterisk(32).Align(HorizontalAlignment.Left).Margin(0, 0, 0, 8),
            Ui.Text("Linework Drawing Companion", 24, Ui.SemiBold),
            Ui.Text($"Version {LineworkVersion.Current}", 14, null, Tokens.Muted),
            Ui.Text("by hyprsea solutions"),
            Ui.Text("s.e.a., your synthetic empathetic ally", 14, null, Tokens.Muted));
        panel.Margin = new Thickness(32);
        return panel;
    }
}
