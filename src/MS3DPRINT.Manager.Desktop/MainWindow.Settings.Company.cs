using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    /// <summary>Company identity shown on the revenue book, the purchase register and their exports.</summary>
    private Border BuildCompanySettingsCard(string root)
    {
        RevenueHeader company;
        try { company = new RevenueHeaderStore(root).Load(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { company = new(); Notify("Les informations de l’entreprise ne peuvent pas être lues : " + e.Message, error: true); }
        var edit = new Button { Content = Icons.Label("IconEdit", "Modifier", 16), Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Left };
        edit.Click += EditRevenueHeader_Click;
        static string Value(string text) => string.IsNullOrWhiteSpace(text) ? "À renseigner" : text.Trim();
        var values = new StackPanel { Spacing = 0, Children =
        {
            SettingsValueRow("Entreprise", Value(company.BusinessName)), SettingsValueRow("Entrepreneur", Value(company.OwnerName)), SettingsValueRow("SIRET", Value(company.Siret)),
            SettingsValueRow("Adresse", Value(company.Address)), SettingsValueRow("E-mail", Value(company.Email)), SettingsValueRow("Téléphone", Value(company.Phone))
        } };
        var header = SettingsCardHeader("IconHome", "Mon entreprise", "Identité de l’atelier imprimée sur le livre des recettes, le registre des achats et leurs exports.", edit);
        return new Border { Classes = { "card-raised" }, Child = new StackPanel { Spacing = 16, Children = { header, values } } };
    }
}
