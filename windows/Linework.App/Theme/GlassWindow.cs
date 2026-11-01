using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Linework.App.Library;
using Linework.Core;

namespace Linework.App.Theme;

/// <summary>Mica behind the ground tint on Windows 11 22H2 and later; solid ground otherwise. The tint's opacity comes from the Transparency setting.</summary>
public class GlassWindow : Window
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }

    const int UseImmersiveDarkMode = 20, SystemBackdropType = 38, BackdropMica = 2;

    public static bool MicaAvailable => Environment.OSVersion.Version.Build >= 22621;

    readonly Settings? _settings;
    readonly Border _tint = new() { Background = Tokens.Ground };
    readonly ContentControl _holder = new();

    public GlassWindow(Settings? settings, bool extendIntoTitleBar = false)
    {
        _settings = settings;
        var root = new Grid();
        root.Children.Add(_tint);
        root.Children.Add(_holder);
        base.Content = root;
        Background = Tokens.Ground;
        if (extendIntoTitleBar)
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 30,
                GlassFrameThickness = MicaAvailable ? new Thickness(-1) : new Thickness(0),
                ResizeBorderThickness = SystemParameters.WindowResizeBorderThickness,
                UseAeroCaptionButtons = false,
                CornerRadius = default,
            });
        if (settings != null) settings.PropertyChanged += (_, _) => ApplyTint();
        Tokens.Changed += OnTheme;
        Closed += (_, _) => Tokens.Changed -= OnTheme;
        SourceInitialized += (_, _) => ApplyBackdrop();
        StateChanged += (_, _) => FitMaximized();
        ApplyTint();
    }

    /// <summary>What shows on the glass.</summary>
    public new object? Content
    {
        get => _holder.Content;
        set => _holder.Content = value;
    }

    void OnTheme()
    {
        ApplyBackdrop();
        ApplyTint();
    }

    void ApplyTint() =>
        _tint.Opacity = MicaAvailable ? Glass.TintOpacity(_settings?.Current.Transparency ?? Glass.DefaultTransparency) : 1;

    void ApplyBackdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = Tokens.IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref dark, sizeof(int));
        if (!MicaAvailable) return;
        Background = Brushes.Transparent;
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var mica = BackdropMica;
        DwmSetWindowAttribute(hwnd, SystemBackdropType, ref mica, sizeof(int));
    }

    /// <summary>A maximized window with no title bar hangs past the screen edge by its resize border.</summary>
    void FitMaximized()
    {
        var border = SystemParameters.WindowResizeBorderThickness;
        _holder.Margin = WindowState == WindowState.Maximized && WindowChrome.GetWindowChrome(this) != null
            ? border
            : default;
    }
}
