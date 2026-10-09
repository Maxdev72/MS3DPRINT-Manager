using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Collections;

namespace MS3DPRINT.Manager.Desktop;

public sealed class EditCollectionWindow : Window
{
    private readonly TextBox _name;
    private readonly TextBox _description;
    private readonly TextBox _notes;
    public string NameValue => _name.Text?.Trim() ?? string.Empty;
    public string? Description => Optional(_description.Text);
    public string? Notes => Optional(_notes.Text);
    public EditCollectionWindow(CollectionItemSummary item)
    {
        Title = "Fiche collection"; Width = 520; Height = 470;
        _name = new TextBox { Text = item.Profile?.Name ?? item.Name, PlaceholderText = "Nom" };
        _description = new TextBox { Text = item.Profile?.Description ?? string.Empty, PlaceholderText = "Description", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 80 };
        _notes = new TextBox { Text = item.Profile?.Notes ?? string.Empty, PlaceholderText = "Notes", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 100 };
        var save = new Button { Content = "Enregistrer", HorizontalAlignment = HorizontalAlignment.Right }; save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(NameValue)) Close(true); };
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12, Children = { new TextBlock { Text = "Fiche collection", FontSize = 20 }, _name, _description, _notes, save } };
    }
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
