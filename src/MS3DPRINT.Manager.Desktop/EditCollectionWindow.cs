using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Collections;

namespace MS3DPRINT.Manager.Desktop;

public sealed class EditCollectionWindow : Window
{
    private readonly TextBox _name;
    private readonly TextBox _description;
    private readonly TextBox _contact;
    private readonly TextBox _address;
    private readonly TextBox _phone;
    private readonly TextBox _email;
    private readonly TextBox _website;
    private readonly TextBox _notes;
    public string NameValue => _name.Text?.Trim() ?? string.Empty;
    public string? Description => Optional(_description.Text);
    public string? Contact => Optional(_contact.Text);
    public string? Address => Optional(_address.Text);
    public string? Phone => Optional(_phone.Text);
    public string? Email => Optional(_email.Text);
    public string? Website => Optional(_website.Text);
    public string? Notes => Optional(_notes.Text);
    public EditCollectionWindow(CollectionItemSummary item)
    {
        Title = "Fiche collection"; Width = 560; Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _name = new TextBox { Text = item.Profile?.Name ?? item.Name, PlaceholderText = "Nom" };
        _description = new TextBox { Text = item.Profile?.Description ?? string.Empty, PlaceholderText = "Description", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 80 };
        _contact = new TextBox { Text = item.Profile?.Contact ?? string.Empty, PlaceholderText = "Contact" };
        _address = new TextBox { Text = item.Profile?.Address ?? string.Empty, PlaceholderText = "Adresse", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 60 };
        _phone = new TextBox { Text = item.Profile?.Phone ?? string.Empty, PlaceholderText = "Téléphone" };
        _email = new TextBox { Text = item.Profile?.Email ?? string.Empty, PlaceholderText = "E-mail" };
        _website = new TextBox { Text = item.Profile?.Website ?? string.Empty, PlaceholderText = "Site web" };
        _notes = new TextBox { Text = item.Profile?.Notes ?? string.Empty, PlaceholderText = "Notes", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 100 };
        var save = new Button { Content = "Enregistrer", HorizontalAlignment = HorizontalAlignment.Right }; save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(NameValue)) Close(true); };
        Content = new ScrollViewer { Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12, Children = { new TextBlock { Text = "Fiche collection", FontSize = 20 }, _name, _description, new TextBlock { Text = "Contact et coordonnées" }, _contact, _address, _phone, _email, _website, new TextBlock { Text = "Notes" }, _notes, save } } };
    }
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
