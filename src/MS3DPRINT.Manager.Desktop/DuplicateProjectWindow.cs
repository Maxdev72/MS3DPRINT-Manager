using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop;

public sealed class DuplicateProjectWindow : DialogWindow
{
    private readonly TextBox _name;
    private readonly CheckBox _files = new() { Content = "Copier aussi les fichiers et sous-dossiers", IsChecked = false };
    public string ProjectName => _name.Text?.Trim() ?? "";
    public bool IncludeFiles => _files.IsChecked == true;

    public DuplicateProjectWindow(string name)
    {
        Width = 520; Height = 470; Title = "Dupliquer le projet";
        _name = new TextBox { Text = name + " — copie" };
        var error = FormKit.ErrorText();
        var confirm = new Button { Content = "Dupliquer le projet" };
        confirm.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(ProjectName)) { error.Text = "Donnez un nom à la copie."; error.IsVisible = true; return; }
            Close(true);
        };
        Layout("Dupliquer le projet", "Une nouvelle référence sera créée pour le même client, avec le statut À faire. La description, les notes et la priorité sont reprises.",
            [FormKit.Field("Nom du nouveau projet", _name), _files, new TextBlock { Text = "Les devis enregistrés restent liés au projet d’origine. Si vous copiez les fichiers, tous les documents du dossier seront repris ; les originaux sont conservés.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" }, LineHeight = 22 }, error], confirm);
        Opened += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }
}
