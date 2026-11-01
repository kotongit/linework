using System.IO;
using System.Windows;
using Linework.App.Library;
using Linework.App.Theme;
using Microsoft.Win32;

namespace Linework.App.Views;

/// <summary>The first screen when no library is open: make a new one or open an existing folder.</summary>
public sealed class LibraryChooserView(LineworkContext context) : StoreView(context.App)
{
    AppModel App => context.App;

    protected override UIElement Render()
    {
        var panel = Ui.Column(24,
            Ui.Asterisk(40).Align(HorizontalAlignment.Left),
            Ui.Text("Your library", 64, Ui.SemiBold),
            Ui.Text("Linework keeps your photos, canvases and progress in a folder you can open any time.", 20, null, Tokens.Muted, 520));
        if (MessageText is { } text) Ui.Add(panel, Ui.Text(text, 15, Ui.Medium, maxWidth: 520), 24);
        var buttons = Ui.Row(12, Ui.Capsule("Create Library", ChooseNew, filled: true), Ui.Capsule("Open Library", ChooseExisting));
        if (CanRetry)
            Ui.Add(buttons, Ui.Capsule("Try Again", () =>
            {
                App.Message = null;
                App.OpenRemembered();
            }), 12);
        Ui.Add(panel, buttons.Margin(0, 8), 24);
        panel.Margin = new Thickness(96, 0, 96, 0);
        panel.VerticalAlignment = VerticalAlignment.Center;
        panel.HorizontalAlignment = HorizontalAlignment.Left;
        Dispatcher.BeginInvoke(AskAboutAdoption);
        return panel;
    }

    void AskAboutAdoption()
    {
        if (App.PendingAdoption is not { } path) return;
        App.PendingAdoption = null;
        if (AlertWindow.Ask("Use this folder for your library?", "It already has other files in it. Linework will add its own folders and won't change what's there.", "Use Folder", "Cancel") == 0)
            App.Create(path);
    }

    bool CanRetry => App.Message is ChooserMissing || (App.Location.HasRemembered && App.Message != null);

    string? MessageText => App.Message switch
    {
        ChooserMissing m => $"Linework can't find “{Path.GetFileName(m.Path.TrimEnd('\\', '/'))}”. It may be on a drive that isn't connected, or the folder may have moved. If it moved, choose Open Library and pick it in its new place.",
        ChooserNewerFormat => "That library was made by a newer version of Linework. Update the app to open it.",
        ChooserFailed f => f.Text,
        _ => null,
    };

    void ChooseNew()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Create Library",
            FileName = "Linework Library",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = false,
            AddExtension = false,
            Filter = "Folder|*.",
        };
        if (dialog.ShowDialog() == true) App.OpenCreating(dialog.FileName);
    }

    void ChooseExisting()
    {
        var dialog = new OpenFolderDialog { Title = "Open Library" };
        if (dialog.ShowDialog() == true) App.Open(dialog.FolderName);
    }
}
