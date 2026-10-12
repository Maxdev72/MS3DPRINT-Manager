using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Desktop;

public sealed class EditClientWindow : DialogWindow
{
    private readonly TextBox _address;
    private readonly TextBox _notes;
    private readonly TextBox _companyName;
    private readonly TextBox _siret;
    private readonly TextBox _identityFirstName;
    private readonly TextBox _identityLastName;
    private readonly TextBox _contactFirstName;
    private readonly TextBox _contactLastName;
    private readonly TextBox _contactRole;
    private readonly TextBox _contactPhone;
    private readonly TextBox _contactEmail;
    public string? Address => Normalize(_address.Text);
    public string? Notes => Normalize(_notes.Text);
    public string? CompanyName => Normalize(_companyName.Text);
    public string? Siret => Normalize(_siret.Text);
    public string? IdentityFirstName => Normalize(_identityFirstName.Text);
    public string? IdentityLastName => Normalize(_identityLastName.Text);
    public string? ContactFirstName => Normalize(_contactFirstName.Text);
    public string? ContactLastName => Normalize(_contactLastName.Text);
    public string? ContactRole => Normalize(_contactRole.Text);
    public string? ContactPhone => Normalize(_contactPhone.Text);
    public string? ContactEmail => Normalize(_contactEmail.Text);

    public EditClientWindow(ClientProfile profile)
    {
        Title = "Fiche client";
        Width = 540;
        Height = 760;
        MinWidth = 440;
        _companyName = new TextBox { Text = profile.CompanyName ?? string.Empty, PlaceholderText = "Raison sociale", IsVisible = profile.Kind == ClientKind.Professional };
        _siret = new TextBox { Text = profile.Siret ?? string.Empty, PlaceholderText = "SIRET", IsVisible = profile.Kind == ClientKind.Professional };
        _identityFirstName = new TextBox { Text = profile.FirstName ?? string.Empty, PlaceholderText = "Prénom", IsVisible = profile.Kind == ClientKind.Individual };
        _identityLastName = new TextBox { Text = profile.LastName ?? string.Empty, PlaceholderText = "Nom", IsVisible = profile.Kind == ClientKind.Individual };
        _address = new TextBox { Text = profile.Address ?? string.Empty, PlaceholderText = "Adresse", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 90 };
        _notes = new TextBox { Text = profile.Notes ?? string.Empty, PlaceholderText = "Notes internes", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 100 };
        _contactFirstName = new TextBox { Text = profile.PrimaryContact.FirstName ?? string.Empty, PlaceholderText = "Prénom du contact" };
        _contactLastName = new TextBox { Text = profile.PrimaryContact.LastName ?? string.Empty, PlaceholderText = "Nom du contact" };
        _contactRole = new TextBox { Text = profile.PrimaryContact.Role ?? string.Empty, PlaceholderText = "Fonction" };
        _contactPhone = new TextBox { Text = profile.PrimaryContact.Phone ?? string.Empty, PlaceholderText = "Téléphone" };
        _contactEmail = new TextBox { Text = profile.PrimaryContact.Email ?? string.Empty, PlaceholderText = "E-mail" };
        var save = new Button { Content = "Enregistrer" };
        save.Click += (_, _) => Close(true);
        Layout(profile.Kind == ClientKind.Professional ? profile.CompanyName ?? profile.FolderName : string.Join(" ", new[] { profile.FirstName, profile.LastName }), "Code client : " + profile.ClientCode,
            [FormKit.SectionLabel("Identité"),
             FormKit.Field("Raison sociale", _companyName), FormKit.Field("SIRET", _siret),
             FormKit.Field("Prénom", _identityFirstName), FormKit.Field("Nom", _identityLastName),
             FormKit.SectionLabel("Contact principal"),
             FormKit.Field("Prénom du contact", _contactFirstName), FormKit.Field("Nom du contact", _contactLastName), FormKit.Field("Fonction", _contactRole),
             FormKit.Field("Téléphone", _contactPhone), FormKit.Field("E-mail", _contactEmail),
             FormKit.SectionLabel("Adresse"), _address,
             FormKit.SectionLabel("Notes internes"), _notes], save);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
