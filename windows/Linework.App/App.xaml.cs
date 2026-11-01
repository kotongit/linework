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
        Tokens.Apply(Tokens.SystemIsDark());
#if DEBUG
        if (e.Args.Contains("--harness"))
        {
            _ = Debugging.Harness.RunAsync(this);
            return;
        }
#endif
        var settings = new Settings(LibraryLocation.DebugOverride != null ? null : Settings.DefaultPath);
        var context = new LineworkContext(new AppModel(new WpfHost(), settings), new SessionStore(new WpfHost()));
        context.App.OpenRemembered();
        var window = new MainWindow(context);
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }
}
