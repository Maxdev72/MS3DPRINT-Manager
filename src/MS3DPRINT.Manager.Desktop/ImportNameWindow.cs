using Avalonia.Controls;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>A folder offered when a file is imported from the project root.</summary>
public sealed record ImportDestination(string Label, string Path);

/// <summary>Imports a file using a project or collection reference, with an optional destination choice.</summary>
public sealed class ImportNameWindow : DialogWindow
{
    private readonly TextBox _name = new();
    private readonly TextBlock _error = FormKit.ErrorText();
    private readonly ComboBox _folder = new() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly IReadOnlyList<ImportDestination>? _destinations;

    public string FileName { get; private set; }

    /// <summary>Full path of the chosen folder, or null when the file goes to the folder that is already open.</summary>
    public string? DestinationDirectory { get; private set; }

    public ImportNameWindow(string originalName, string suggestedName, string projectReference,
        IReadOnlyList<ImportDestination>? destinations = null, string? suggestedDestination = null, int fileNumber = 1, int totalFiles = 1, bool isCollection = false)
    {
        FileName = originalName;
        _destinations = destinations;
        var chooseFolder = destinations is { Count: > 0 };
        if (chooseFolder)
        {
            _folder.ItemsSource = destinations!.Select(item => item.Label).ToArray();
            _folder.SelectedIndex = Math.Max(0, destinations!.ToList().FindIndex(item => string.Equals(item.Path, suggestedDestination, StringComparison.OrdinalIgnoreCase)));
        }
        Title = "Importer un fichier";
        Width = 580; Height = chooseFolder ? 600 : 480; MinWidth = 460;
        _name.Text = suggestedName;

        var accept = new Button { Content = "Importer avec ce nom" };
        accept.Click += (_, _) =>
        {
            var chosen = _name.Text?.Trim() ?? string.Empty;
            if (chosen.Length == 0 || chosen.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                FormKit.ShowError(_error, _name, "Ce nom est vide ou contient un caractère interdit.");
                return;
            }
            FileName = chosen;
            DestinationDirectory = ChosenDestination();
            Close(true);
        };
        var keep = new Button { Content = "Garder le nom d’origine" };
        keep.Click += (_, _) => { FileName = originalName; DestinationDirectory = ChosenDestination(); Close(true); };

        var body = new List<Control>
        {
            FormKit.Field("Nom d’origine", new TextBlock { Text = originalName, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold }),
            FormKit.Field("Nom proposé", _name, hint: $"Convention : nom du fichier + « __ » + référence {(isCollection ? "de la collection" : "du projet")} ({projectReference}).")
        };
        if (chooseFolder) body.Add(FormKit.Field("Dossier de destination", _folder, hint: "Proposé selon le type de fichier."));
        body.Add(_error);
        Button? cancel = null;
        if (totalFiles > 1)
        {
            accept.Content = "Importer ce fichier";
            cancel = new Button { Content = "Arrêter l’import" };
            cancel.Click += (_, _) => Close(false);
        }
        Layout(totalFiles > 1 ? $"Importer · fichier {fileNumber} sur {totalFiles}" : isCollection ? "Importer dans la collection" : "Importer dans le projet", "Vous pouvez renommer le fichier pour qu’il respecte la convention de nommage.", body, accept, cancel, extraAction: keep);
        Opened += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    private string? ChosenDestination()
        => _destinations is { Count: > 0 } && _folder.SelectedIndex is var index and >= 0 ? _destinations[index].Path : null;
}
