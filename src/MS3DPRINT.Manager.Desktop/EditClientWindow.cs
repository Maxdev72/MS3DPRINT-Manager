using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Desktop;

public sealed class EditClientWindow : Window
{
    private readonly TextBox _address;
    private readonly TextBox _notes;
    private readonly TextBox _contactFirstName;
    private readonly TextBox _contactLastName;
    private readonly TextBox _contactRole;
    private readonly TextBox _contactPhone;
    private readonly TextBox _contactEmail;
    public string? Address => Normalize(_address.Text);
    public string? Notes => Normalize(_notes.Text);
    public string? ContactFirstName => Normalize(_contactFirstName.Text);
    public string? ContactLastName => Normalize(_contactLastName.Text);
    public string? ContactRole => Normalize(_contactRole.Text);
    public string? ContactPhone => Normalize(_contactPhone.Text);
    public string? ContactEmail => Normalize(_contactEmail.Text);

    public EditClientWindow(ClientProfile profile)
    {
        Title = "Fiche client";
        Width = 520;
        Height = 680;
        MinWidth = 420;
        _address = new TextBox { Text = profile.Address ?? string.Empty, PlaceholderText = "Adresse", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 90 };
        _notes = new TextBox { Text = profile.Notes ?? string.Empty, PlaceholderText = "Notes internes", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 100 };
        _contactFirstName = new TextBox { Text = profile.PrimaryContact.FirstName ?? string.Empty, PlaceholderText = "Prénom du contact" };
        _contactLastName = new TextBox { Text = profile.PrimaryContact.LastName ?? string.Empty, PlaceholderText = "Nom du contact" };
        _contactRole = new TextBox { Text = profile.PrimaryContact.Role ?? string.Empty, PlaceholderText = "Fonction" };
        _contactPhone = new TextBox { Text = profile.PrimaryContact.Phone ?? string.Empty, PlaceholderText = "Téléphone" };
        _contactEmail = new TextBox { Text = profile.PrimaryContact.Email ?? string.Empty, PlaceholderText = "E-mail" };
        var save = new Button { Content = "Enregistrer", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24), Spacing = 12,
            Children =
            {
                new TextBlock { Text = profile.Kind == ClientKind.Professional ? profile.CompanyName ?? profile.FolderName : string.Join(" ", new[] { profile.FirstName, profile.LastName }), FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = profile.ClientCode, Classes = { "muted" } },
                new TextBlock { Text = "Contact" }, _contactFirstName, _contactLastName, _contactRole, _contactPhone, _contactEmail,
                new TextBlock { Text = "Adresse" }, _address,
                new TextBlock { Text = "Notes" }, _notes, save
            }
        };
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
