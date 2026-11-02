using System.Windows;
using Linework.App.Library;
using Linework.App.Platform;
using Linework.App.Theme;
using Linework.App.Views;

namespace Linework.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // Menus, dropdowns, checkboxes and scroll bars follow the system's light or dark.
        ThemeMode = ThemeMode.System;
        Tokens.Apply(Tokens.SystemIsDark());
#if DEBUG
        if (e.Args.Contains("--harness"))
        {
            _ = Debugging.Harness.RunAsync(this);
            return;
        }
#endif
        var context = Start();
        // Whatever goes wrong in a view, what's unsaved gets saved rather than lost with the process.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Linework");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "errors.log"), $"{DateTimeOffset.Now:O} {args.Exception}\n");
                context.App.Library?.Flush();
                context.App.Freehand?.Flush();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Telling the user still comes first.
            }
            if (context.App.Library is { } library) library.Notice = "Something went wrong, and your changes were saved. If it keeps happening, restart Linework.";
        };
        var window = new MainWindow(context);
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }

    static LineworkContext Start()
    {
        var settings = new Settings(LibraryLocation.DebugOverride != null ? null : Settings.DefaultPath);
        var context = new LineworkContext(new AppModel(new WpfHost(), settings), new SessionStore(new WpfHost()));
        context.App.OpenRemembered();
        return context;
    }
}
