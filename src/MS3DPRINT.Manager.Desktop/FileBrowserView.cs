using Avalonia.Controls;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Automation;
using Avalonia.VisualTree;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Desktop;

public sealed record FileBrowserState(string Query, FileFamily Family, int Page, Vector Offset);

public sealed partial class FileBrowserView : StackPanel
{
    private readonly List<ProjectFileEntry> _entries;
    private readonly Func<ProjectFileEntry, Control> _row;
    private readonly Func<Control> _header;
    private readonly Button _clear;
    private bool _loading;
    private int? _restorePage;
    private Vector? _restoreOffset;
    public TextBox SearchInput { get; } = new() { PlaceholderText = "Rechercher dans ce dossier…" };
    public ComboBox FamilyInput { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    public StackPanel Rows { get; } = new();
    public TextBlock StatusLabel { get; } = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

    public FileBrowserView(IReadOnlyList<ProjectFileEntry> entries, Func<ProjectFileEntry, Control> row, Func<Control> header, Func<Task> reload,
        FileBrowserState? state = null, bool loading = false)
    {
        _entries = new(entries); _row = row; _header = header; _loading = loading;
        _restorePage = state?.Page; _restoreOffset = state?.Offset;
        Spacing = 12;
        AutomationProperties.SetName(SearchInput, "Rechercher un fichier ou un dossier");
        AutomationProperties.SetName(FamilyInput, "Filtrer les fichiers par format");
        AutomationProperties.SetLiveSetting(StatusLabel, AutomationLiveSetting.Polite);
        FamilyInput.ItemsSource = Enum.GetValues<FileFamily>(); FamilyInput.SelectedItem = state?.Family ?? FileFamily.All;
        SearchInput.Text = state?.Query ?? "";
        FamilyInput.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<FileFamily>((family, _) => new TextBlock { Text = FileCatalogFilter.Label(family) });
        var clear = new Button { Content = "Effacer", Classes = { "ghost" } };
        _clear = clear;
        var refresh = new Button { Content = Icons.Glyph("IconRefresh", 17), Classes = { "tool" } };
        AutomationProperties.SetName(clear, "Effacer les filtres des fichiers");
        AutomationProperties.SetName(refresh, "Actualiser les fichiers"); ToolTip.SetTip(refresh, "Actualiser les fichiers");
        refresh.Click += async (_, _) => { refresh.IsEnabled = false; try { await reload(); } finally { refresh.IsEnabled = true; } };
        void Reset() { SearchInput.Text = ""; FamilyInput.SelectedItem = FileFamily.All; SearchInput.Focus(); }
        clear.Click += (_, _) => Reset();
        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,180,Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 8, RowSpacing = 8 };
        Grid.SetColumn(FamilyInput, 1); Grid.SetColumn(clear, 2); Grid.SetColumn(refresh, 3);
        filters.Children.Add(SearchInput); filters.Children.Add(FamilyInput); filters.Children.Add(clear); filters.Children.Add(refresh);
        filters.SizeChanged += (_, _) =>
        {
            var narrow = filters.Bounds.Width < 620;
            Grid.SetColumnSpan(SearchInput, narrow ? 4 : 1);
            Grid.SetRow(FamilyInput, narrow ? 1 : 0); Grid.SetColumn(FamilyInput, narrow ? 0 : 1); Grid.SetColumnSpan(FamilyInput, narrow ? 2 : 1);
            Grid.SetRow(clear, narrow ? 1 : 0); Grid.SetRow(refresh, narrow ? 1 : 0);
        };
        Children.Add(filters); Children.Add(StatusLabel); InitializeSelection(filters); Children.Add(Rows);
        void FiltersChanged() { ClearSelection(); _restorePage = null; _restoreOffset = null; Render(); }
        SearchInput.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) FiltersChanged(); };
        FamilyInput.SelectionChanged += (_, _) => FiltersChanged();
        SearchInput.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) { Reset(); e.Handled = true; } };
        Render();
    }

    public FileBrowserState CaptureState() => new(SearchInput.Text ?? "", FamilyInput.SelectedItem is FileFamily family ? family : FileFamily.All,
        _restorePage ?? PagedRows.GetPage(Rows), _restoreOffset ?? this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault()?.Offset ?? default);

    public void AddEntries(IReadOnlyList<ProjectFileEntry> entries, bool complete)
    {
        var fillingFirstPage = _entries.Count < PagedRows.PageSize;
        _entries.AddRange(entries);
        _loading = !complete;
        if (complete) _entries.Sort((a, b) => a.IsDirectory != b.IsDirectory ? (a.IsDirectory ? -1 : 1) : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        // Keep the first page still while reading; filters can inspect all entries received so far.
        if (fillingFirstPage || complete || !string.IsNullOrEmpty(SearchInput.Text) || FamilyInput.SelectedItem is FileFamily family && family != FileFamily.All)
            Render();
        else StatusLabel.Text = $"Lecture du dossier… {_entries.Count:N0} éléments reçus · affichage partiel";
        if (complete && _restoreOffset is { } offset)
        {
            _restoreOffset = null;
            Dispatcher.UIThread.Post(() =>
            {
                if (IsEffectivelyVisible && TopLevel.GetTopLevel(this) is not null && this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
                { scroll.UpdateLayout(); scroll.Offset = offset; }
            }, DispatcherPriority.Loaded);
        }
    }

    public void ShowReadError(Control recovery)
    {
        _loading = false; _entries.Clear(); Rows.Children.Clear();
        ClearSelection();
        SearchInput.IsEnabled = FamilyInput.IsEnabled = _clear.IsEnabled = false;
        StatusLabel.Text = "Lecture interrompue : le contenu du dossier n’a pas pu être chargé complètement.";
        Rows.Children.Add(recovery);
    }

    private void Render()
    {
            var family = FamilyInput.SelectedItem is FileFamily selected ? selected : FileFamily.All;
            var query = SearchInput.Text ?? "";
            var matches = FileCatalogFilter.Filter(_entries, query, family);
            _clear.IsEnabled = query.Length > 0 || family != FileFamily.All;
            var files = matches.Count(e => !e.IsDirectory); var folders = matches.Count - files;
            StatusLabel.Text = _loading ? $"Lecture du dossier… {_entries.Count:N0} éléments reçus · résultats partiels" :
                matches.Count == 0 ? (_entries.Count == 0 ? "Ce dossier est vide." : "Aucun résultat dans ce dossier.") :
                $"{files:N0} fichier(s) · {folders:N0} dossier(s)" + (_clear.IsEnabled ? $" · sur {_entries.Count:N0} éléments" : "");
            if (matches.Count == 0)
            {
                Rows.Children.Clear();
                if (_loading) return;
                var reset = new Button { Content = "Effacer les filtres", HorizontalAlignment = HorizontalAlignment.Left };
                reset.Click += (_, _) => { SearchInput.Text = ""; FamilyInput.SelectedItem = FileFamily.All; SearchInput.Focus(); };
                Rows.Children.Add(new StackPanel { Spacing = 10, Margin = new Thickness(14, 12), Children =
                { new TextBlock { Text = _entries.Count == 0 ? "Importez un fichier ou créez un dossier avec les boutons ci-dessus." : "Essayez un autre nom ou choisissez tous les formats.", TextWrapping = TextWrapping.Wrap, Classes = { "muted" } } } });
                if (_entries.Count != 0) ((StackPanel)Rows.Children[0]).Children.Add(reset);
            }
            else PagedRows.Render(Rows, matches, SelectionRow, SelectionHeader, (query, family), _loading ? null : _restorePage);
            if (!_loading) _restorePage = null;
    }
}
