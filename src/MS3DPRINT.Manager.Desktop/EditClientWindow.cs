using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Desktop;

public sealed class EditClientWindow : Window
{
    private readonly TextBox _address;
    private readonly TextBox _notes;
    public string? Address => Normalize(_address.Text);
    public string? Notes => Normalize(_notes.Text);

    public EditClientWindow(ClientProfile profile)
    {
        Title = "Fiche client";
        Width = 520;
        Height = 430;
        MinWidth = 420;
        _address = new TextBox { Text = profile.Address ?? string.Empty, PlaceholderText = "Adresse", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 90 };
        _notes = new TextBox { Text = profile.Notes ?? string.Empty, PlaceholderText = "Notes internes", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 100 };
        var save = new Button { Content = "Enregistrer", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24), Spacing = 12,
            Children =
            {
                new TextBlock { Text = profile.Kind == ClientKind.Professional ? profile.CompanyName ?? profile.FolderName : string.Join(" ", new[] { profile.FirstName, profile.LastName }), FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = profile.ClientCode, Classes = { "muted" } },
                new TextBlock { Text = "Adresse" }, _address,
                new TextBlock { Text = "Notes" }, _notes, save
            }
        };
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
