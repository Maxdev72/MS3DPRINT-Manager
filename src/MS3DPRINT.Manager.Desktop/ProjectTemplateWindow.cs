using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Templates;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Editor of the folders created in every new project, and of where the imported files are sorted.</summary>
public sealed class ProjectTemplateWindow : DialogWindow
{
    private static readonly ProjectFileCategory?[] Categories =
        [null, ProjectFileCategory.Documents, ProjectFileCategory.ClientFiles, ProjectFileCategory.Step, ProjectFileCategory.Stl, ProjectFileCategory.ThreeMf];

    private sealed class Row
    {
        public required TextBox Path { get; init; }
        public required ComboBox Category { get; init; }
    }

    private readonly List<Row> _rows = [];
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly TextBlock _error = FormKit.ErrorText();

    public ProjectTemplate? Result { get; private set; }

    /// <summary>True when the user asked to go back to the standard template.</summary>
    public bool ResetRequested { get; private set; }

    public ProjectTemplateWindow(ProjectTemplate current)
    {
        Title = "Arborescence des projets";
        Width = 600; Height = 700; MinWidth = 440;
        Fill(current);
        var add = new Button { Content = "Ajouter un dossier", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => { _rows.Add(NewRow(new ProjectTemplateFolder(string.Empty))); Rebuild(); _rows[^1].Path.Focus(); };
        var save = new Button { Content = "Enregistrer" };
        save.Click += (_, _) => Save();
        var reset = new Button { Content = "Rétablir le modèle standard" };
        reset.Click += async (_, _) =>
        {
            if (!await ConfirmRemovalWindow.AskAsync(reset, "Arborescence personnalisée", "Le modèle standard remplacera votre arborescence personnalisée. Les projets existants seront conservés.")) return;
            ResetRequested = true; Close(true);
        };
        var hint = new TextBlock
        {
            Text = "Écrivez « Dossier/Sous-dossier » pour créer un sous-dossier. La catégorie indique où sont classés les fichiers importés : un seul dossier par catégorie.",
            Classes = { "faint" }, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        Layout("Arborescence des projets", "Dossiers créés dans chaque nouveau projet. Les projets existants ne changent pas.",
            [hint, _list, add, _error], save, extraAction: reset);
        Rebuild();
    }

    private void Fill(ProjectTemplate template)
    {
        _rows.Clear();
        foreach (var folder in template.Folders) _rows.Add(NewRow(folder));
    }

    private static Row NewRow(ProjectTemplateFolder folder)
    {
        var category = new ComboBox
        {
            ItemsSource = Categories,
            SelectedItem = folder.Category,
            Width = 150,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ProjectFileCategory?>((value, _) =>
                new TextBlock { Text = value is { } known ? ProjectTemplate.Label(known) : "Aucune catégorie" })
        };
        return new Row { Path = new TextBox { Text = folder.Path, PlaceholderText = "ex. 08_NOTES" }, Category = category };
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        for (var index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];
            var position = index;
            var up = Tool("IconChevronUp", "Monter", position > 0, () => Move(position, -1));
            var down = Tool("IconChevronDown", "Descendre", position < _rows.Count - 1, () => Move(position, 1));
            var remove = Tool("IconTrash", "Retirer ce dossier", true);
            remove.Click += async (_, _) =>
            {
                if (!await ConfirmRemovalWindow.AskAsync(remove, string.IsNullOrWhiteSpace(row.Path.Text) ? "Dossier du modèle" : row.Path.Text, "Cette ligne sera retirée du modèle des prochains projets. Les dossiers et fichiers existants seront conservés.")) return;
                _rows.Remove(row); Rebuild();
            };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), ColumnSpacing = 6 };
            row.Path.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(row.Category, 1);
            Grid.SetColumn(up, 2);
            Grid.SetColumn(down, 3);
            Grid.SetColumn(remove, 4);
            foreach (var control in new Control[] { row.Path, row.Category, up, down, remove }) grid.Children.Add(control);
            _list.Children.Add(grid);
        }
    }

    private static Button Tool(string icon, string tip, bool enabled, Action? action = null)
    {
        var button = new Button { Content = Icons.Glyph(icon, 16), Classes = { "icon-action" }, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        if (action is not null) button.Click += (_, _) => action();
        return button;
    }

    private void Move(int index, int offset)
    {
        var target = index + offset;
        if (target < 0 || target >= _rows.Count) return;
        (_rows[index], _rows[target]) = (_rows[target], _rows[index]);
        Rebuild();
    }

    private void Save()
    {
        var template = new ProjectTemplate(_rows
            .Select(row => new ProjectTemplateFolder((row.Path.Text ?? string.Empty).Trim().Replace('\\', '/').Trim('/'), row.Category.SelectedItem as ProjectFileCategory?))
            .ToArray());
        if (template.Validate() is { } problem)
        {
            _error.Text = problem;
            _error.IsVisible = true;
            return;
        }
        Result = template;
        Close(true);
    }
}
