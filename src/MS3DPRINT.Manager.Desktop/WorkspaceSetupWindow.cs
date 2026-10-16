using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>First launch: where to keep clients, projects and collections. Synchronised folders found on the computer are offered first.</summary>
public sealed class WorkspaceSetupWindow : DialogWindow
{
    private readonly List<(RadioButton Button, string? Root)> _choices = [];

    /// <summary>The workspace folder chosen; null when the user asked to pick another folder.</summary>
    public string? SelectedRoot => _choices.FirstOrDefault(choice => choice.Button.IsChecked == true).Root;

    public WorkspaceSetupWindow(IReadOnlyList<SyncFolder> detected, string localRoot)
    {
        Title = "Bienvenue";
        Width = 560; Height = 640; MinWidth = 440;
        var list = new StackPanel { Spacing = 10 };
        foreach (var folder in detected)
            Add(list, StorageProviderCatalog.Name(folder.Kind) + " · synchronisé", SyncFolderDetector.WorkspaceIn(folder), SyncFolderDetector.WorkspaceIn(folder));
        Add(list, "Dossier local", localRoot, localRoot);
        Add(list, "Un autre dossier…", "Vous choisirez l’emplacement.", null);
        _choices[0].Button.IsChecked = true;

        var start = new Button { Content = "Commencer" };
        start.Click += (_, _) => Close(true);
        // there is no way to refuse: the software needs a folder, and the first choice is a sensible one
        var hiddenCancel = new Button { IsVisible = false };
        Layout("Où ranger vos clients et projets ?",
            "Un dossier ordinaire sur cet ordinateur. Si vous choisissez un dossier synchronisé par Nextcloud, Google Drive ou Dropbox, leur application s’occupe de la synchronisation. Vous pourrez changer dans les Paramètres.",
            [list], start, hiddenCancel);
    }

    private void Add(StackPanel list, string title, string detail, string? root)
    {
        var text = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.SemiBold }, new TextBlock { Text = detail, Classes = { "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap } } };
        var button = new RadioButton { GroupName = "workspace", Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        _choices.Add((button, root));
        list.Children.Add(button);
    }
}
