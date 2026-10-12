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

    public string FileName { get; private set; }

    public ImportNameWindow(string originalName, string suggestedName, string projectReference)
    {
        FileName = originalName;
        Title = "Importer un fichier";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 560; Height = 400; MinWidth = 440;
        _name.Text = suggestedName;

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
            Close(true);
        };
        var keep = new Button { Content = "Garder le nom d’origine" };
        keep.Click += (_, _) => { FileName = originalName; Close(true); };
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
                _error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, keep, accept } }
            }
        };
    }
}
