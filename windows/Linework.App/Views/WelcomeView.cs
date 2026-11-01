using System.Windows;
using Linework.App.Theme;
using Linework.Core.Copy;

namespace Linework.App.Views;

/// <summary>First-launch screen: s.e.a. introduces itself and asks for the user's name.</summary>
public sealed class WelcomeView : StoreView
{
    readonly LineworkContext _context;
    readonly System.Windows.Controls.Button _continue;
    readonly System.Windows.Controls.TextBox _box;
    readonly UIElement _body;

    public WelcomeView(LineworkContext context)
    {
        _context = context;
        var (field, box) = Ui.UnderlinedField(40, LineworkCopy.MaxNameLength, "Your name", 520);
        _box = box;
        _continue = Ui.Capsule("Continue", Done, filled: true);
        _continue.IsEnabled = false;
        box.TextChanged += (_, _) => _continue.IsEnabled = Trimmed.Length > 0;
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) Done();
        };
        var panel = Ui.Column(24,
            Ui.Asterisk(40).Align(HorizontalAlignment.Left),
            Ui.Text("Hi, I'm s.e.a.", 64, Ui.SemiBold),
            Ui.Text("Your synthetic empathetic ally. I'll be here while you practice. What should I call you?", 20, null, Tokens.Muted, 520),
            field,
            Ui.Row(20, _continue, Ui.Link("Skip for now", Skip, 14, Ui.Medium, Tokens.Muted).Align(HorizontalAlignment.Left, VerticalAlignment.Center)).Margin(0, 8));
        panel.Margin = new Thickness(96, 0, 96, 0);
        panel.VerticalAlignment = VerticalAlignment.Center;
        panel.HorizontalAlignment = HorizontalAlignment.Left;
        _body = panel;
        Content = panel;
        Loaded += (_, _) => box.Focus();
    }

    string Trimmed => LineworkCopy.NormalizedName(_box.Text);

    void Done()
    {
        if (Trimmed.Length == 0) return;
        var name = Trimmed;
        _context.Settings.Update(v => v with { Name = name, Welcomed = true });
    }

    void Skip() => _context.Settings.Update(v => v with { Welcomed = true });

    protected override UIElement Render() => _body;
}
