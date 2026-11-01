using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace Linework.App.Theme;

/// <summary>Small builders for the app's look: the capsule, circle and pill shapes, and text in its font.</summary>
public static class Ui
{
    public static TextBlock Text(string text, double size = 14, FontWeight? weight = null, Brush? color = null, double? maxWidth = null, bool wrap = false, bool tabular = false)
    {
        var t = new TextBlock
        {
            Text = text,
            FontFamily = Tokens.Font,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = color ?? Tokens.Ink,
            TextWrapping = wrap || maxWidth != null ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        if (maxWidth != null) t.MaxWidth = maxWidth.Value;
        if (tabular) Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        TextOptions.SetTextFormattingMode(t, size >= 20 ? TextFormattingMode.Ideal : TextFormattingMode.Display);
        return t;
    }

    /// <summary>Long text the reader can select and copy.</summary>
    public static TextBox SelectableText(string text, double size = 14, Brush? color = null)
    {
        var box = PlainText(text, size, color);
        TextBlock.SetLineHeight(box, size * 1.4);
        return box;
    }

    static TextBox PlainText(string text, double size, Brush? color) => new()
    {
        Style = new Style(typeof(TextBox)),
        Text = text,
        IsReadOnly = true,
        FontFamily = Tokens.Font,
        FontSize = size,
        Foreground = color ?? Tokens.Ink,
        CaretBrush = Brushes.Transparent,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        TextWrapping = TextWrapping.Wrap,
    };

    public static readonly FontWeight Medium = FontWeights.Medium, SemiBold = FontWeights.SemiBold;

    /// <summary>Only the shape it's given, dimmed when pressed or disabled. Colours stay out of the style, which would freeze them.</summary>
    static readonly Style PlainButton = MakePlainButton();

    static Style MakePlainButton()
    {
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        var template = new ControlTemplate(typeof(Button)) { VisualTree = content };
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, true));
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.6));
        style.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
        style.Triggers.Add(disabled);
        return style;
    }

    public static Button Plain(UIElement content, Action click, string? label = null)
    {
        var button = new Button { Style = PlainButton, Content = content, Cursor = Cursors.Hand };
        button.Click += (_, _) => click();
        if (label != null)
        {
            AutomationProperties(button, label);
            button.ToolTip = label;
        }
        return button;
    }

    static void AutomationProperties(DependencyObject o, string name) => System.Windows.Automation.AutomationProperties.SetName(o, name);

    /// <summary>Capsule button: 1pt ink border, 12pt medium uppercase. Filled inverts to ink fill.</summary>
    public static Button Capsule(string title, Action click, double height = 44, bool filled = false)
    {
        var button = Plain(CapsuleBody(title, height, filled), click);
        AutomationProperties(button, title);
        return button;
    }

    public static Border CapsuleBody(string title, double height, bool filled) => new()
    {
        MinHeight = height,
        CornerRadius = new CornerRadius(height / 2),
        BorderThickness = new Thickness(1),
        BorderBrush = Tokens.Ink,
        Background = filled ? Tokens.Ink : Brushes.Transparent,
        Padding = new Thickness(18, 0, 18, 0),
        Child = Centered(Text(title.ToUpperInvariant(), 12, Medium, filled ? Tokens.Ground : Tokens.Ink)),
    };

    static T Centered<T>(T element) where T : FrameworkElement
    {
        element.HorizontalAlignment = HorizontalAlignment.Center;
        element.VerticalAlignment = VerticalAlignment.Center;
        return element;
    }

    /// <summary>Round icon button with a 1pt ink border.</summary>
    public static Button Circle(UIElement icon, Action click, string label, double size = 44)
    {
        if (icon is FrameworkElement e) Centered(e);
        return Plain(new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            BorderThickness = new Thickness(1),
            BorderBrush = Tokens.Ink,
            Background = Brushes.Transparent,
            Child = icon,
        }, click, label);
    }

    /// <summary>Non-interactive label in the same capsule shape.</summary>
    public static Border Pill(string text) => new()
    {
        CornerRadius = new CornerRadius(14),
        BorderThickness = new Thickness(1),
        BorderBrush = Tokens.Ink,
        Padding = new Thickness(12, 5, 12, 5),
        VerticalAlignment = VerticalAlignment.Center,
        Child = Text(text.ToUpperInvariant(), 12, Medium),
    };

    /// <summary>Text with no chrome that acts as a button, as SwiftUI's plain style.</summary>
    public static Button Link(string title, Action click, double size = 14, FontWeight? weight = null, Brush? color = null, bool underline = false)
    {
        var text = Text(title, size, weight ?? Medium, color);
        if (underline) text.TextDecorations = TextDecorations.Underline;
        var button = Plain(new Border { Background = Brushes.Transparent, Child = text }, click);
        AutomationProperties(button, title);
        return button;
    }

    /// <summary>Six-armed mark: three crossing 2pt strokes. Decorative.</summary>
    public static FrameworkElement Asterisk(double size = 24, Brush? color = null)
    {
        var geometry = new GeometryGroup();
        double c = size / 2, r = size / 2 - 1;
        for (var k = 0; k < 3; k++)
        {
            var a = k * Math.PI / 3 + Math.PI / 2;
            geometry.Children.Add(new LineGeometry(new Point(c + r * Math.Cos(a), c + r * Math.Sin(a)), new Point(c - r * Math.Cos(a), c - r * Math.Sin(a))));
        }
        return new Path
        {
            Data = geometry,
            Stroke = color ?? Tokens.Ink,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = size,
            Height = size,
        };
    }

    /// <summary>A shape drawn in ink from path markup, on a 24-point square.</summary>
    public static FrameworkElement Icon(string data, double size = 16, Brush? color = null, bool stroke = false, double weight = 1.5)
    {
        var path = new Path { Data = Geometry.Parse(data), Stretch = Stretch.None };
        if (stroke)
        {
            path.Stroke = color ?? Tokens.Ink;
            path.StrokeThickness = weight;
            path.StrokeStartLineCap = PenLineCap.Round;
            path.StrokeEndLineCap = PenLineCap.Round;
            path.StrokeLineJoin = PenLineJoin.Round;
        }
        else
        {
            path.Fill = color ?? Tokens.Ink;
        }
        return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
    }

    public const string MoonIcon = "M20 14.5A8.5 8.5 0 1 1 9.5 4a7 7 0 0 0 10.5 10.5z";
    public const string CheckIcon = "M5 12.5l4.5 4.5L19 7.5";
    public const string EllipsisIcon = "M5 10.5a1.5 1.5 0 1 0 0 3a1.5 1.5 0 1 0 0-3zM12 10.5a1.5 1.5 0 1 0 0 3a1.5 1.5 0 1 0 0-3zM19 10.5a1.5 1.5 0 1 0 0 3a1.5 1.5 0 1 0 0-3z";
    public const string StarIcon = "M12 3l2.7 5.8 6.3.8-4.6 4.3 1.2 6.3L12 17l-5.6 3.2 1.2-6.3L3 9.6l6.3-.8z";
    public const string ChevronLeftIcon = "M15 5L8 12L15 19";
    public const string ChevronRightIcon = "M9 5L16 12L9 19";
    public const string FlameIcon = "M12 2c1 4 6 6 6 12a6 6 0 0 1-12 0c0-3 2-4 2.5-7 1.5 1 2.5 2.5 2.5 4 1-2 1.5-5 1-9z";
    public const string ArrowDownRightIcon = "M6 6L18 18M18 8V18H8";

    public static StackPanel Row(double spacing, params UIElement[] children) => Stack(Orientation.Horizontal, spacing, children);
    public static StackPanel Column(double spacing, params UIElement[] children) => Stack(Orientation.Vertical, spacing, children);

    static StackPanel Stack(Orientation orientation, double spacing, UIElement[] children)
    {
        var panel = new StackPanel { Orientation = orientation };
        foreach (var child in children) Add(panel, child, spacing);
        return panel;
    }

    /// <summary>Appends with the gap before it, unless it's first.</summary>
    public static void Add(StackPanel panel, UIElement child, double spacing)
    {
        if (panel.Children.Count > 0 && spacing != 0 && child is FrameworkElement e)
        {
            var m = e.Margin;
            e.Margin = panel.Orientation == Orientation.Horizontal
                ? new Thickness(m.Left + spacing, m.Top, m.Right, m.Bottom)
                : new Thickness(m.Left, m.Top + spacing, m.Right, m.Bottom);
        }
        panel.Children.Add(child);
    }

    public static T Margin<T>(this T element, double left, double top = 0, double right = 0, double bottom = 0) where T : FrameworkElement
    {
        element.Margin = new Thickness(left, top, right, bottom);
        return element;
    }

    public static T Align<T>(this T element, HorizontalAlignment h = HorizontalAlignment.Stretch, VerticalAlignment v = VerticalAlignment.Stretch) where T : FrameworkElement
    {
        element.HorizontalAlignment = h;
        element.VerticalAlignment = v;
        return element;
    }

    /// <summary>A plain text field with an ink rule under it.</summary>
    public static (StackPanel Panel, TextBox Box) UnderlinedField(double size, int maxLength, string label, double? width = null)
    {
        var box = new TextBox
        {
            // An empty style keeps this plain, without the system theme's box.
            Style = new Style(typeof(TextBox)),
            FontFamily = Tokens.Font,
            FontSize = size,
            FontWeight = Medium,
            Foreground = Tokens.Ink,
            CaretBrush = Tokens.Ink,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            MaxLength = maxLength,
        };
        AutomationProperties(box, label);
        var panel = Column(6, box, new Rectangle { Fill = Tokens.Ink, Height = 1 });
        if (width != null)
        {
            panel.Width = width.Value;
            panel.HorizontalAlignment = HorizontalAlignment.Left;
        }
        return (panel, box);
    }

    public static CheckBox Toggle(string title, bool isOn, Action<bool> changed)
    {
        var box = new CheckBox { Content = Text(title), IsChecked = isOn, Foreground = Tokens.Ink, VerticalContentAlignment = VerticalAlignment.Center };
        box.Click += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    /// <summary>A toggle drawn as a capsule: filled in ink when on.</summary>
    public static Button CapsuleToggle(string title, bool isOn, Action<bool> changed, double height = 30) =>
        Capsule(title, () => changed(!isOn), height, isOn);

    public static Border Rule(bool vertical = false) => vertical
        ? new Border { Width = 1, Background = Tokens.Hairline }
        : new Border { Height = 1, Background = Tokens.Hairline };
}
