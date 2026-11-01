#if DEBUG
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Linework.App.Library;
using Linework.App.Platform;
using Linework.App.Theme;
using Linework.App.Views;

namespace Linework.App.Debugging;

/// <summary>Debug-only: Linework --harness with LINEWORK_LIBRARY set to a scratch folder. Drives real views and stores, prints a line per check,
/// and writes screenshots to LINEWORK_SHOTS.</summary>
public static partial class Harness
{
    static int _failures;
    static string _shots = "";
    static Window? _stage;

    public static void Check(string name, bool ok)
    {
        if (!ok) _failures++;
        Console.Out.WriteLine($"check {(ok ? "pass" : "FAIL")} {name}");
        Console.Out.Flush();
    }

    public static async Task RunAsync(Application app)
    {
        var path = Environment.GetEnvironmentVariable("LINEWORK_LIBRARY");
        if (string.IsNullOrEmpty(path))
        {
            // The harness overwrites and clears things, so it must never reach real data.
            Console.Error.WriteLine("This needs LINEWORK_LIBRARY set to a scratch folder. Nothing was run.");
            app.Shutdown(2);
            return;
        }
        try
        {
            AlertWindow.Silent = true;
            app.DispatcherUnhandledException += (_, e) =>
            {
                Check($"Unhandled: {e.Exception}", false);
                e.Handled = true;
            };
            _shots = Environment.GetEnvironmentVariable("LINEWORK_SHOTS") ?? Path.Combine(path, "shots");
            Directory.CreateDirectory(_shots);
            Directory.CreateDirectory(path);
            Check("Inter Tight loads", Tokens.Font.GetTypefaces().Count() >= 3);
            StoreChecks.Run(Path.Combine(path, "checks", "library"), new WpfHost(), Check);
            SessionChecks.Run(Path.Combine(path, "session-checks", "library"), new WpfHost(), Check);
            await Stages(path);
        }
        catch (Exception e)
        {
            Check($"Harness ran ({e})", false);
        }
        _stage?.Close();
        Console.Out.WriteLine($"harness done, {_failures} failed");
        Console.Out.Flush();
        app.Shutdown(_failures == 0 ? 0 : 1);
    }

    public static async Task Settle(double seconds = 0.15)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Shows the element off screen so it loads for real, then renders it to a PNG. Offscreen renders don't prove glass or DPI behaviour.</summary>
    public static async Task Shoot(string name, FrameworkElement element, double w, double h)
    {
        _stage ??= new Window
        {
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -20000,
            Top = -20000,
        };
        _stage.Width = w;
        _stage.Height = h;
        _stage.Background = Tokens.Ground;
        _stage.Content = new Border { Background = Tokens.Ground, Child = element };
        if (!_stage.IsVisible) _stage.Show();
        await Settle(0.4);
        foreach (var theme in new[] { false, true })
        {
            Tokens.Apply(theme);
            await Settle(0.1);
            var root = (FrameworkElement)_stage.Content;
            var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using var file = File.Create(Path.Combine(_shots, $"{name}-{(theme ? "dark" : "light")}.png"));
            encoder.Save(file);
        }
        _stage.Content = null;
    }

    public static LineworkContext Context(string? name = "Ana", bool welcomed = true)
    {
        var settings = new Settings(null);
        settings.Update(v => v with { Name = name ?? "", Welcomed = welcomed });
        var host = new WpfHost();
        return new LineworkContext(new AppModel(host, settings), new SessionStore(host));
    }

    /// <summary>The real main window, opened and routed, so nothing in it can fail only when it's seen.</summary>
    static async Task ShellStage(LineworkContext context)
    {
        var window = new MainWindow(context) { Left = 40, Top = 40, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show();
        await Settle(1);
        await ShootWindow("shell-menu", window);
        foreach (var section in AppSections.Visible)
        {
            context.Session.SetRoute(new AppRoute(section));
            await Settle(1);
            await ShootWindow($"shell-{section}", window);
        }
        context.Session.SetRoute(AppRoute.Menu);
        await Settle(0.2);
        Check("The main window routes", window.IsVisible);
        window.Close();
        Check("The main window closes", !window.IsVisible);
    }

    static async Task Stages(string path)
    {
        var context = Context();
        await Shoot("chooser", new LibraryChooserView(context), 1280, 770);
        context.App.Message = new ChooserMissing(Path.Combine(path, "Moved Library"));
        await Shoot("chooser-missing", new LibraryChooserView(context), 1280, 770);
        var root = Path.Combine(path, "shots-library");
        context.App.Create(root);
        var library = context.App.Library!;
        await Shoot("welcome", new WelcomeView(Context("", false)), 1280, 770);
        await Shoot("menu", new MainMenuView(context, library, () => { }, _ => { }, () => new DateTime(2026, 11, 2, 9, 41, 7)), 1280, 770);
        await Shoot("menu-small", new MainMenuView(context, library, () => { }, _ => { }, () => new DateTime(2026, 11, 2, 20, 44, 13)), 1100, 670);
        await Shoot("settings", SettingsWindow.Body(context), 420, 340);
        await Shoot("about", AboutWindow.Body(), 420, 240);
        await Shoot("topbar", new TopBar(context, library, () => { }), 1280, 52);
        await PhotoStages(path, context, library);
        await PlanStages(context, library);
        await SessionStages(context, library);
        await ShellStage(context);
        Check("Screenshots were written", Directory.GetFiles(_shots, "*.png").Length >= 12);
    }
}
#endif
