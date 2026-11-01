using System.Windows;
using Linework.App.Theme;

namespace Linework.App.Views;

/// <summary>A question with its own button titles, which the system message box can't do.</summary>
public sealed class AlertWindow : GlassWindow
{
    /// <summary>Tests answer with the last button, the cautious one in every question asked.</summary>
    public static bool Silent { get; set; }

    int _answer;

    AlertWindow(string title, string text, string[] buttons) : base(null)
    {
        _answer = buttons.Length - 1;
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var row = Ui.Row(12).Align(HorizontalAlignment.Right);
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            Ui.Add(row, Ui.Capsule(buttons[i], () =>
            {
                _answer = index;
                Close();
            }, 36, i == 0), 12);
        }
        Content = Ui.Column(12,
            Ui.Text(title, 16, Ui.SemiBold, maxWidth: 364),
            Ui.Text(text, 14, null, Tokens.Muted, 364),
            row.Margin(0, 8)).Margin(28, 24, 28, 24);
    }

    /// <summary>Asks for a line of text. Null if cancelled.</summary>
    public static string? AskText(string title, string initial, string confirm = "Save", string cancel = "Cancel")
    {
        if (Silent) return null;
        var (field, box) = Ui.UnderlinedField(16, 200, title);
        box.Text = initial;
        box.SelectAll();
        var window = new AlertWindow(title, "", [confirm, cancel]) { Owner = Application.Current?.MainWindow };
        var body = (System.Windows.Controls.StackPanel)window.Content!;
        body.Children.RemoveAt(1);
        body.Children.Insert(1, field);
        box.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            window._answer = 0;
            window.Close();
        };
        window.Loaded += (_, _) => box.Focus();
        window.ShowDialog();
        return window._answer == 0 ? box.Text : null;
    }

    /// <summary>Returns which button was chosen; closing the window counts as the last.</summary>
    public static int Ask(string title, string text, params string[] buttons)
    {
        if (Silent) return buttons.Length - 1;
        var window = new AlertWindow(title, text, buttons) { Owner = Application.Current?.MainWindow };
        window.ShowDialog();
        return window._answer;
    }
}
