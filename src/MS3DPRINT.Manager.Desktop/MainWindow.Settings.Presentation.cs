using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using System.Globalization;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<Border, string> _settingsIndex = new();
    private static Control SettingsCardHeader(string icon, string title, string description, Control? action = null)
    {
        var heading = new StackPanel { Spacing = 6 };
        heading.Children.Add(new TextBlock { Text = title, Classes = { "card-title" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(description)) heading.Children.Add(new TextBlock { Text = description, Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 14, LineHeight = 22 });
        var grid = new Grid { Classes = { "settings-card-heading" }, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 12, RowSpacing = 10 };
        var glyph = Icons.Glyph(icon, 21); glyph.VerticalAlignment = VerticalAlignment.Top; glyph.Margin = new Thickness(0, 3, 0, 0);
        glyph.Classes.Add("settings-heading-icon");
        Grid.SetColumn(heading, 1); grid.Children.Add(glyph); grid.Children.Add(heading);
        if (action is not null)
        {
            Grid.SetColumn(action, 2); action.VerticalAlignment = VerticalAlignment.Top; grid.Children.Add(action);
            grid.SizeChanged += (_, _) =>
            {
                var narrow = grid.Bounds.Width < 600;
                Grid.SetRow(action, narrow ? 1 : 0); Grid.SetColumn(action, narrow ? 1 : 2);
                Grid.SetColumnSpan(action, narrow ? 2 : 1);
                action.HorizontalAlignment = HorizontalAlignment.Left;
            };
        }
        return grid;
    }

    private void SettingsSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        if (StorageList is null) return;
        UpdateSettingsCategory(); SettingsScroll.Offset = default;
    }
    private void ClearSettingsSearch_Click(object? sender, RoutedEventArgs e) { SettingsSearchBox.Text = ""; SettingsSearchBox.Focus(); }
    private void SettingsSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && _settingsMatches.Count > 0)
        {
            ShowSettingsMatch(_settingsMatchIndex + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1));
            e.Handled = true; return;
        }
        if (e.Key != Key.Escape || string.IsNullOrEmpty(SettingsSearchBox.Text)) return;
        ClearSettingsSearch_Click(sender, new RoutedEventArgs()); e.Handled = true;
    }
    private string SettingsSearchText(Border card)
    {
        if (_settingsIndex.TryGetValue(card, out var text)) return text;
        text = SettingsCategoryText(card.Tag as string ?? "").Title + " " + string.Join(" ", card.GetLogicalDescendants().Select(c => c switch
        {
            TextBlock t => t.Text, Button { Content: string label } => label, _ => null
        }).Where(t => !string.IsNullOrWhiteSpace(t)));
        _settingsIndex[card] = text;
        return text;
    }

    private static (string Title, string Detail) SettingsCategoryText(string category) => category switch
    {
        "organisation" => ("Organisation", "Dossiers et classement"),
        "storage" => ("Espace et stockage", "Données et sauvegardes"),
        "application" => ("Application", "Performances et version"),
        _ => ("Atelier", "Tarifs et production")
    };

    private void InitializeSettingsPresentation()
    {
        foreach (var button in SettingsCategories.Children.OfType<Button>())
        {
            var (title, detail) = SettingsCategoryText((string)button.Tag!);
            var icon = (button.Tag as string) switch { "organisation" => "IconProjects", "storage" => "IconCollections", "application" => "IconSettings", _ => "IconPrinters" };
            var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 9 };
            heading.Children.Add(Icons.Glyph(icon, 17));
            var caption = new TextBlock { Text = title, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            Grid.SetColumn(caption, 1); heading.Children.Add(caption);
            button.Content = new StackPanel { Spacing = 5, Children =
            {
                heading,
                new TextBlock { Text = detail, Classes = { "settings-nav-detail", "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 13, LineHeight = 18 }
            } };
            Avalonia.Automation.AutomationProperties.SetName(button, title);
        }
        SizeChanged += (_, _) => UpdateSettingsLayout();
        UpdateSettingsHeading();
        UpdateSettingsLayout();
    }

    private void UpdateSettingsHeading()
    {
        SettingsSectionTitle.Text = SettingsCategoryText(_settingsCategory).Title;
        SettingsDescription.Text = _settingsCategory switch
        {
            "organisation" => "Choisissez les dossiers à créer et le classement des fichiers pour vos prochains projets.",
            "storage" => "Retrouvez vos données, créez une sauvegarde ou restaurez une copie de votre espace de travail.",
            "application" => "Adaptez les performances et consultez les informations du logiciel.",
            _ => "Définissez vos tarifs et vos hypothèses de production une fois, pour vos prochains devis."
        };
    }

    private void UpdateSettingsLayout()
    {
        var compact = (Bounds.Width > 0 ? Bounds.Width : Width) < 1250;
        SettingsLayout.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 204);
        SettingsLayout.ColumnSpacing = compact ? 0 : 24;
        Grid.SetColumn(SettingsCategories, compact ? 1 : 0);
        Grid.SetRowSpan(SettingsCategories, compact ? 1 : 3);
        SettingsCategories.Width = compact ? double.NaN : 204;
        Grid.SetRow(SettingsHeading, compact ? 2 : 1);
        Grid.SetRow(SettingsScroll, compact ? 3 : 2);
        Grid.SetRowSpan(SettingsScroll, compact ? 1 : 2);
        foreach (var button in SettingsCategories.Children.OfType<Button>())
        {
            button.Width = compact ? double.NaN : 204;
            button.Padding = compact ? new Thickness(12, 10) : new Thickness(14, 14);
            button.Margin = compact ? new Thickness(0, 0, 6, 6) : new Thickness(0, 0, 0, 8);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            if (button.Content is StackPanel labels && labels.Children.LastOrDefault() is TextBlock detail)
                detail.IsVisible = !compact;
        }
    }
}
