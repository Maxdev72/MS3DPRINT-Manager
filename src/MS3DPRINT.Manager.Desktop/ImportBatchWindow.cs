using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop;

public sealed record ImportBatchItem(string Source, string FileName, string Directory);
public sealed record ImportedSource(string Source, string Imported);

/// <summary>Review every name and destination before starting any copies.</summary>
public sealed class ImportBatchWindow : DialogWindow
{
    public IReadOnlyList<ImportBatchItem> Items { get; private set; } = [];

    public ImportBatchWindow(IReadOnlyList<ImportBatchItem> items, IReadOnlyList<ImportDestination>? destinations = null, string? reference = null)
    {
        Title = "Importer plusieurs fichiers"; Width = 600; Height = 760; MinWidth = 460;
        var error = FormKit.ErrorText();
        var rows = new List<(ImportBatchItem Item, TextBox Name, ComboBox Folder)>();
        var list = new StackPanel { Spacing = 18 };
        foreach (var item in items)
        {
            var name = new TextBox { Text = item.FileName };
            var folder = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            if (destinations is { Count: > 0 })
            {
                folder.ItemsSource = destinations.Select(d => d.Label).ToArray();
                folder.SelectedIndex = Math.Max(0, destinations.ToList().FindIndex(d => string.Equals(d.Path, item.Directory, StringComparison.OrdinalIgnoreCase)));
            }
            rows.Add((item, name, folder));
            var original = new TextBlock { Text = Path.GetFileName(item.Source), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
            ToolTip.SetTip(original, item.Source);
            var row = new StackPanel { Spacing = 8, Children = { original, FormKit.Field("Nom à l’import", name) } };
            Avalonia.Automation.AutomationProperties.SetName(name, "Nom à l’import de " + Path.GetFileName(item.Source));
            if (destinations is { Count: > 0 }) row.Children.Add(FormKit.Field("Destination", folder));
            else
            {
                var location = new TextBlock { Text = "Destination : « " + Path.GetFileName(Path.TrimEndingDirectorySeparator(item.Directory)) + " »", TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "caption" } };
                ToolTip.SetTip(location, item.Directory);
                Avalonia.Automation.AutomationProperties.SetName(location, "Destination : " + item.Directory);
                row.Children.Add(location);
            }
            list.Children.Add(row);
        }
        var keepNames = new Button { Content = "Garder tous les noms d’origine", Classes = { "ghost" } };
        keepNames.Click += (_, _) => { foreach (var row in rows) row.Name.Text = Path.GetFileName(row.Item.Source); };
        var accept = new Button { Content = $"Importer les {items.Count} fichiers" };
        accept.Click += (_, _) =>
        {
            var result = new List<ImportBatchItem>();
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var name = row.Name.Text?.Trim() ?? "";
                if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
                { FormKit.ShowError(error, row.Name, "Choisissez un nom de fichier valide, sans caractère interdit ni point final."); return; }
                var directory = destinations is { Count: > 0 } && row.Folder.SelectedIndex >= 0 ? destinations[row.Folder.SelectedIndex].Path : row.Item.Directory;
                if (!targets.Add(Path.GetFullPath(Path.Combine(directory, name))))
                { FormKit.ShowError(error, row.Name, "Deux fichiers du lot ont le même nom dans la même destination. Modifiez ce nom."); return; }
                result.Add(row.Item with { FileName = name, Directory = directory });
            }
            Items = result;
            Close(true);
        };
        Layout($"Importer {items.Count} fichiers", reference is null
            ? "Vérifiez tous les noms avant de lancer les copies. Les fichiers existants ne seront jamais écrasés."
            : $"Les noms proposés utilisent la référence {reference}. Vous pouvez les modifier avant de lancer les copies, sans écraser les fichiers existants.",
            [keepNames, list, error], accept);
        Opened += (_, _) => rows.FirstOrDefault().Name?.Focus();
    }
}

/// <summary>One explicit destructive confirmation for the successful copies of this batch only.</summary>
public sealed class ConfirmImportSourcesWindow : DialogWindow
{
    public IReadOnlyList<ImportedSource> Selected { get; private set; } = [];

    public ConfirmImportSourcesWindow(IReadOnlyList<ImportedSource> copies)
    {
        Title = "Sources des fichiers importés"; Width = 600; Height = 720; MinWidth = 460;
        var checks = new List<(ImportedSource Copy, CheckBox Check)>();
        var list = new StackPanel { Spacing = 18 };
        var count = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
        var delete = new Button { Content = "Supprimer les sources sélectionnées" };
        var all = new CheckBox { Content = "Sélectionner toutes les sources", IsChecked = true };
        var updatingSelection = false;
        void Update()
        {
            var selected = checks.Count(row => row.Check.IsChecked == true);
            count.Text = $"{selected} source{(selected > 1 ? "s" : "")} sélectionnée{(selected > 1 ? "s" : "")} sur {copies.Count}.";
            delete.IsEnabled = selected > 0;
            if (!updatingSelection)
            {
                updatingSelection = true;
                all.IsChecked = selected == 0 ? false : selected == copies.Count ? true : null;
                updatingSelection = false;
            }
        }
        foreach (var copy in copies)
        {
            var check = new CheckBox { Content = Path.GetFileName(copy.Source), IsChecked = true, Tag = copy.Source };
            Avalonia.Automation.AutomationProperties.SetName(check, "Supprimer l’original " + copy.Source);
            checks.Add((copy, check)); check.IsCheckedChanged += (_, _) => Update();
            list.Children.Add(new StackPanel { Spacing = 6, Children =
            {
                check,
                FormKit.Field("Source à supprimer", new SelectableTextBlock { Text = copy.Source, TextWrapping = TextWrapping.Wrap }),
                FormKit.Field("Copie conservée", new SelectableTextBlock { Text = copy.Imported, TextWrapping = TextWrapping.Wrap })
            } });
        }
        all.IsCheckedChanged += (_, _) =>
        {
            if (updatingSelection) return;
            updatingSelection = true;
            foreach (var row in checks) row.Check.IsChecked = all.IsChecked == true;
            updatingSelection = false;
            Update();
        };
        var keep = new Button { Content = "Conserver les sources" };
        keep.Click += (_, _) => Close(false);
        delete.Click += (_, _) => { Selected = checks.Where(row => row.Check.IsChecked == true).Select(row => row.Copy).ToArray(); Close(true); };
        Update();
        Layout("Supprimer les sources du lot ?", "Les copies ci-dessous ont réussi. Les originaux sélectionnés seront supprimés définitivement après vérification de leur copie. Les sources des imports échoués sont conservées.",
            [all, count, list], delete, keep, destructive: true);
    }
}
