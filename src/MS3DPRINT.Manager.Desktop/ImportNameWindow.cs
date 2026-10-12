using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Asks whether an imported file should be renamed to follow the project's naming rule.</summary>
public sealed class ImportNameWindow : Window
{
    private readonly TextBox _name = new();
    private readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, IsVisible = false, TextWrapping = TextWrapping.Wrap };

    private readonly ComboBox _folder = new() { MinWidth = 300 };
    private readonly IReadOnlyList<ImportDestination>? _destinations;

    public string FileName { get; private set; }

    /// <summary>Full path of the chosen folder, or null when the file goes to the folder that is already open.</summary>
    public string? DestinationDirectory { get; private set; }

    public ImportNameWindow(string originalName, string suggestedName, string projectReference,
        IReadOnlyList<ImportDestination>? destinations = null, string? suggestedDestination = null)
    {
        FileName = originalName;
        _destinations = destinations;
        if (destinations is { Count: > 0 })
        {
            _folder.ItemsSource = destinations.Select(item => item.Label).ToArray();
            _folder.SelectedIndex = Math.Max(0, destinations.ToList().FindIndex(item => string.Equals(item.Path, suggestedDestination, StringComparison.OrdinalIgnoreCase)));
        }
        Title = "Importer un fichier";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 560; Height = destinations is { Count: > 0 } ? 500 : 400; MinWidth = 440;
        _name.Text = suggestedName;
        _folder.IsVisible = destinations is { Count: > 0 };

        var accept = new Button { Content = "Importer avec ce nom", Classes = { "primary-action" } };
        accept.Click += (_, _) =>
        {
            var chosen = _name.Text?.Trim() ?? string.Empty;
            if (chosen.Length == 0 || chosen.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                _error.Text = "Ce nom est vide ou contient un caractère interdit.";
                _error.IsVisible = true;
                return;
            }
            FileName = chosen;
            DestinationDirectory = ChosenDestination();
            Close(true);
        };
        var keep = new Button { Content = "Garder le nom d’origine" };
        keep.Click += (_, _) => { FileName = originalName; DestinationDirectory = ChosenDestination(); Close(true); };
        var cancel = new Button { Content = "Annuler" };
        cancel.Click += (_, _) => Close(false);

        var original = new TextBlock { Text = originalName, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
        var hint = new TextBlock
        {
            Text = $"Convention : nom du fichier + « __ » + référence du projet ({projectReference}). Vous pouvez modifier le nom proposé.",
            Classes = { "muted" }, TextWrapping = TextWrapping.Wrap
        };
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Renommer à l’import ?", FontSize = 20, FontWeight = FontWeight.SemiBold },
                hint,
                new TextBlock { Text = "Nom d’origine", Classes = { "muted" } }, original,
                new TextBlock { Text = "Nom proposé", Classes = { "muted" } }, _name,
                new TextBlock { Text = "Dossier de destination", Classes = { "muted" }, IsVisible = destinations is { Count: > 0 } },
                _folder,
                _error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, keep, accept } }
            }
        };
    }

    private string? ChosenDestination()
        => _destinations is { Count: > 0 } && _folder.SelectedIndex is var index and >= 0 ? _destinations[index].Path : null;
}

/// <summary>A folder offered when a file is imported from the project root.</summary>
public sealed record ImportDestination(string Label, string Path);
