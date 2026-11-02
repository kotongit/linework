using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Linework.App.Library;
using Linework.App.Theme;
using Linework.Core.Copy;
using Microsoft.Win32;

namespace Linework.App.Views;

/// <summary>The main window: welcome on first launch, otherwise the main menu or a section under the top bar.</summary>
public sealed class MainWindow : GlassWindow
{
    readonly LineworkContext _context;
    readonly ContentControl _page = new();
    readonly Border _noticeHolder = new() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16) };
    readonly Grid _overlays = new();
    object? _shownKey;
    LibraryStore? _watched;
    readonly DispatcherTimer _dayTimer = new() { Interval = TimeSpan.FromMinutes(1) };

    AppModel App => _context.App;
    SessionStore Session => _context.Session;

    public MainWindow(LineworkContext context) : base(context.Settings, true)
    {
        _context = context;
        Title = "Linework";
        Width = 1280;
        Height = 800;
        MinWidth = 1100;
        MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        layout.RowDefinitions.Add(new RowDefinition());
        var bar = TitleRow();
        Grid.SetRow(_page, 1);
        layout.Children.Add(bar);
        layout.Children.Add(_page);
        Grid.SetRow(_overlays, 1);
        layout.Children.Add(_overlays);
        Grid.SetRow(_noticeHolder, 1);
        layout.Children.Add(_noticeHolder);
        Content = layout;

        App.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Route);
        Session.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Route);
        context.Settings.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Route);

        Activated += (_, _) =>
        {
            Session.RefreshToday();
            App.Library?.ReloadIfChanged();
            App.Freehand?.RefreshList();
            Scan();
        };
        Deactivated += (_, _) =>
        {
            App.Library?.Flush();
            App.Freehand?.Flush();
        };
        Closing += (_, e) => e.Cancel = !App.SaveOrConfirmLosing();
        PreviewKeyDown += OnKey;
        _dayTimer.Tick += (_, _) => Session.RefreshToday();
        _dayTimer.Start();
        SystemEvents.TimeChanged += OnTimeChanged;
        SystemEvents.UserPreferenceChanged += OnPreference;
        Closed += (_, _) =>
        {
            SystemEvents.TimeChanged -= OnTimeChanged;
            SystemEvents.UserPreferenceChanged -= OnPreference;
            _dayTimer.Stop();
        };
        Route();
    }

    void OnTimeChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => Session.RefreshToday());

    void OnPreference(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General) Dispatcher.BeginInvoke(() => Tokens.Apply(Tokens.SystemIsDark()));
    }

    /// <summary>For the harness: the row holding File and Go, as drawn.</summary>
    public FrameworkElement? MenuRow { get; private set; }

    FrameworkElement TitleRow()
    {
        var dock = new DockPanel();
        var caption = CaptionButtons();
        DockPanel.SetDock(caption, Dock.Right);
        dock.Children.Add(caption);
        // Plain text buttons, each opening a menu: WPF's Menu control draws nothing in this row under the system theme.
        Button? file = null, go = null;
        file = MenuTitle("File", () => OpenMenu(FileMenu(), file!));
        go = MenuTitle("Go", () => OpenMenu(GoMenu(), go!));
        _fileTitle = file;
        _goTitle = go;
        var row = Ui.Row(0, file, go).Margin(8);
        row.HorizontalAlignment = HorizontalAlignment.Left;
        MenuRow = row;
        dock.Children.Add(row);
        return dock;
    }

    Button? _fileTitle, _goTitle;

    static Button MenuTitle(string title, Action open)
    {
        var body = new Border { Height = 30, Padding = new Thickness(10, 0, 10, 0), Background = Brushes.Transparent, Child = Ui.Text(title, 12).Align(HorizontalAlignment.Center, VerticalAlignment.Center) };
        body.MouseEnter += (_, _) => body.Background = Tokens.Hairline;
        body.MouseLeave += (_, _) => body.Background = Brushes.Transparent;
        var button = Ui.Plain(body, open);
        button.Cursor = System.Windows.Input.Cursors.Arrow;
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        return button;
    }

    static void OpenMenu(ContextMenu menu, FrameworkElement title)
    {
        menu.PlacementTarget = title;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>For the harness.</summary>
    public ContextMenu OpenFileMenuForCheck()
    {
        var menu = FileMenu();
        OpenMenu(menu, _fileTitle!);
        return menu;
    }

    bool Welcoming => App.Library == null || LineworkCopy.ShouldShowWelcome(_context.Settings.Current.Name, _context.Settings.Current.Welcomed);

    static MenuItem Item(string title, Action click, string? gesture = null, bool enabled = true)
    {
        var item = new MenuItem { Header = title, InputGestureText = gesture ?? "", IsEnabled = enabled };
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>Built as it opens, so what's enabled is current.</summary>
    ContextMenu FileMenu()
    {
        var file = new ContextMenu();
        file.Items.Add(Item("Open Library…", App.CloseForSwitch, enabled: !Session.IsSessionActive));
        file.Items.Add(Item("Reveal Library in File Explorer", Reveal, enabled: App.Library != null));
        file.Items.Add(new Separator());
        file.Items.Add(Item("Import Photos…", ImportPhotos, "Ctrl+I", App.Library != null && !Session.IsSessionActive));
        file.Items.Add(new Separator());
        file.Items.Add(Item("Settings…", OpenSettings, "Ctrl+,"));
        file.Items.Add(Item("About Linework", () => new AboutWindow(_context.Settings) { Owner = this }.Show()));
        file.Items.Add(new Separator());
        file.Items.Add(Item("Exit", Close, "Alt+F4"));
        return file;
    }

    ContextMenu GoMenu()
    {
        var go = new ContextMenu();
        go.Items.Add(Item("Main Menu", () => Session.SetRoute(AppRoute.Menu), "Ctrl+0", !Welcoming));
        foreach (var section in AppSections.Visible)
            go.Items.Add(Item(section.Title(), () => Session.SetRoute(new AppRoute(section)), $"Ctrl+{(int)section + 1}", !Welcoming));
        go.Items.Add(new Separator());
        go.Items.Add(Item("Start Session", StartSession, "Ctrl+R", !Welcoming && !Session.IsSessionActive));
        return go;
    }

    void OnKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Alt && e.Key == System.Windows.Input.Key.System
            && e.SystemKey is System.Windows.Input.Key.F or System.Windows.Input.Key.G)
        {
            if (e.SystemKey == System.Windows.Input.Key.F) OpenMenu(FileMenu(), _fileTitle!);
            else OpenMenu(GoMenu(), _goTitle!);
            e.Handled = true;
            return;
        }
        if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control) return;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case System.Windows.Input.Key.I when App.Library != null && !Session.IsSessionActive: ImportPhotos(); break;
            case System.Windows.Input.Key.R when !Welcoming && !Session.IsSessionActive: StartSession(); break;
            case System.Windows.Input.Key.OemComma: OpenSettings(); break;
            case System.Windows.Input.Key.D0 when !Welcoming: Session.SetRoute(AppRoute.Menu); break;
            case >= System.Windows.Input.Key.D1 and <= System.Windows.Input.Key.D6 when !Welcoming:
                var section = (AppSection)(key - System.Windows.Input.Key.D1);
                if (section.IsVisible()) Session.SetRoute(new AppRoute(section));
                break;
            default: return;
        }
        e.Handled = true;
    }

    void Reveal()
    {
        if (App.Library is { } library) Process.Start("explorer.exe", $"\"{library.Root}\"");
    }

    void ImportPhotos()
    {
        Session.SetRoute(new AppRoute(AppSection.Library));
        // After the Library appears, if it wasn't showing.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, Session.RequestImport);
    }

    void OpenSettings() => new SettingsWindow(_context) { Owner = this }.Show();

    public void StartSession()
    {
        if (App.Library is { } library) new SessionSetupWindow(_context, library) { Owner = this }.ShowDialog();
    }

    void Scan()
    {
        if (App.Library is { } library) _ = PhotoScanner.RunAsync(library, App.Host);
    }

    FrameworkElement CaptionButtons()
    {
        Button Caption(string glyph, string label, Action click, bool close = false)
        {
            var body = new Border { Width = 46, Height = 30, Background = Brushes.Transparent };
            body.Child = new TextBlock { Text = glyph, FontFamily = Tokens.Icons, FontSize = 10, Foreground = Tokens.Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var hover = close ? new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)) : Tokens.Hairline;
            body.MouseEnter += (_, _) => body.Background = hover;
            body.MouseLeave += (_, _) => body.Background = Brushes.Transparent;
            var button = Ui.Plain(body, click, label);
            button.Cursor = System.Windows.Input.Cursors.Arrow;
            button.ToolTip = null;
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            return button;
        }
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Caption("", "Minimize", () => SystemCommands.MinimizeWindow(this)));
        panel.Children.Add(Caption("", "Maximize", () =>
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }));
        panel.Children.Add(Caption("", "Close", Close, true));
        return panel;
    }

    string? BottomText(LibraryStore library) =>
        library.SaveFailure ?? library.Notice ?? (library.DamagedFiles.Count > 0 ? $"“{library.DamagedFiles[0]}” couldn't be read, so it was set aside with “damaged” in its name." : null);

    void ShowNotice()
    {
        if (App.Library is { } library && BottomText(library) is { } text)
            _noticeHolder.Child = new Border
            {
                CornerRadius = new CornerRadius(18),
                Background = Tokens.Ground,
                BorderBrush = Tokens.Ink,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 8, 16, 8),
                Child = Ui.Text(text, 13, Ui.Medium),
            };
        else
            _noticeHolder.Child = null;
    }

    void Route()
    {
        if (!ReferenceEquals(_watched, App.Library))
        {
            if (_watched != null) _watched.PropertyChanged -= OnLibraryChanged;
            _watched = App.Library;
            if (_watched != null)
            {
                _watched.PropertyChanged += OnLibraryChanged;
                Scan();
            }
        }
        ShowNotice();
        ShowCelebration();

        object key;
        Func<UIElement> make;
        if (App.Library is not { } library)
        {
            key = "chooser";
            make = () => new LibraryChooserView(_context);
        }
        else if (LineworkCopy.ShouldShowWelcome(_context.Settings.Current.Name, _context.Settings.Current.Welcomed))
        {
            key = "welcome";
            make = () => new WelcomeView(_context);
        }
        else
        {
            key = (library, Session.Route);
            make = () => Page(library);
        }
        if (Equals(key, _shownKey)) return;
        _shownKey = key;
        _page.Content = make();
    }

    /// <summary>Waits for the session window to close, so it's seen rather than played behind it.</summary>
    void ShowCelebration()
    {
        if (Session.PendingCelebration is { } kind && !Session.IsSessionActive)
        {
            if (_overlays.Children.Count > 0) return;
            _overlays.Children.Add(new CelebrationView(kind, Session.PendingCelebrationIsToday, _context.Settings.Current.Sound, () =>
            {
                _overlays.Children.Clear();
                Session.ClearCelebration();
            }));
        }
        else if (Session.PendingCelebration == null)
        {
            _overlays.Children.Clear();
        }
    }

    void OnLibraryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(ShowNotice);

    UIElement Page(LibraryStore library)
    {
        if (Session.Route.Section is not { } section)
            return new MainMenuView(_context, library, OpenSettings, StartNext);
        var panel = new DockPanel();
        var bar = new TopBar(_context, library, StartSession);
        DockPanel.SetDock(bar, Dock.Top);
        panel.Children.Add(bar);
        panel.Children.Add(Section(section, library));
        return panel;
    }

    void StartNext(Linework.Core.Plan.PlanDayRef r)
    {
        if (App.Library is not { } library) return;
        if (library.Schedule.NextTimedBlock(r, library.TickKeys) is { } block && PlanStarter.Start(block, r, library, _context)) return;
        Session.SetRoute(new AppRoute(AppSection.Today));
    }

    UIElement Section(AppSection section, LibraryStore library) => section switch
    {
        AppSection.Today => new TodayView(_context, library),
        AppSection.Path => new PathView(_context, library),
        AppSection.Calendar => new CalendarHeatmapView(_context, library),
        AppSection.Library => new LibraryView(_context, library),
        AppSection.Progress => new BenchmarkProgressView(_context, library),
        AppSection.Freehand => new FreehandView(_context, library, App.Freehand!),
        _ => new Grid(),
    };
}
