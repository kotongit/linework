#if DEBUG
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Linework.App.Library;
using Linework.App.Platform;
using Linework.App.Views;
using Linework.Core.Session;

namespace Linework.App.Debugging;

public static partial class Harness
{
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out SessionPlayerWindow.Rect32 rect);

    static void Press(Window window, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        window.RaiseEvent(args);
    }

    static string PowerRequests()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powercfg", "/requests") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : "";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return "";
        }
    }

    static async Task SessionStages(LineworkContext context, LibraryStore library)
    {
        SoundCue.Muted = true;
        var setup = new SessionSetupWindow(context, library);
        var setupBody = setup.ContentForShot;
        setup.Content = null;
        await Shoot("session-setup", setupBody, 520, 640);
        setup.Close();
        context.Settings.Update(v => v with { SetupClassMode = true });
        var classSetup = new SessionSetupWindow(context, library);
        var classBody = classSetup.ContentForShot;
        classSetup.Content = null;
        await Shoot("session-setup-class", classBody, 520, 640);
        classSetup.Close();
        context.Settings.Update(v => v with { SetupClassMode = false });

        // The real player, on screen.
        var anchor = new Window { Width = 300, Height = 200, WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowActivated = true };
        anchor.Show();
        context.Session.Queue = new SessionQueue(library.VisiblePhotos.Select(p => new Pose(p.Id, 6, null, null)).ToList(), false);
        context.Session.PlanBlock = null;
        context.Session.SoundOn = false;
        var before = library.Sessions.Count;
        var player = SessionPlayerWindow.Open(context, library, anchor);
        await Settle(1);
        var hwnd = new WindowInteropHelper(player).Handle;
        GetWindowRect(hwnd, out var rect);
        var m = player.MonitorRect;
        var dpi = VisualTreeHelper.GetDpi(player);
        Check("PlayerCoversItsMonitorAtDevicePixels", m.Right > m.Left && rect.Left == m.Left && rect.Top == m.Top && rect.Right == m.Right && rect.Bottom == m.Bottom
            && player.ScreenPixels == Math.Max(m.Right - m.Left, m.Bottom - m.Top)
            && Math.Abs(player.ActualWidth * dpi.DpiScaleX - (m.Right - m.Left)) < 2);
        Check("KeepAwake is held in a session", KeepAwake.IsHeld);
        var requests = PowerRequests();
        Console.Out.WriteLine(requests.Length == 0 ? "check note powercfg was denied, relying on the flag" : $"check note powercfg lists us: {requests.Contains("Linework", StringComparison.OrdinalIgnoreCase)}");
        await Settle(3);
        Check("The pose image renders", player.PoseImage.Source != null);
        await ShootWindow("player", player);
        Press(player, Key.Space);
        Check("Space pauses", player.Session.Runner.IsPaused);
        Press(player, Key.Space);
        Press(player, Key.G);
        Press(player, Key.Right);
        Check("Right moves on", player.Session.Runner.Index == 1);
        await Settle(6);
        Press(player, Key.Escape);
        await Settle(0.3);
        Check("Esc records", library.Sessions.Count == before + 1);
        Check("KeepAwake is released", !KeepAwake.IsHeld);
        await ShootWindow("player-summary", player);
        player.Close();
        await Settle(0.3);
        Check("Closing ends the session", !context.Session.IsSessionActive);
        anchor.Close();
    }

    static async Task ShootWindow(string name, Window window)
    {
        await Settle(0.2);
        var content = (FrameworkElement)window.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(Math.Max((int)content.ActualWidth, 1), Math.Max((int)content.ActualHeight, 1), 96, 96, PixelFormats.Pbgra32);
        var layer = new DrawingVisual();
        using (var dc = layer.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        bitmap.Render(layer);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        await using var file = File.Create(Path.Combine(_shots, name + ".png"));
        encoder.Save(file);
    }
}
#endif
