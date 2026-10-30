using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Desktop;

public sealed partial class FileBrowserView
{
    private readonly HashSet<string> _selectedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _selectionLabel = new() { VerticalAlignment = VerticalAlignment.Center, Classes = { "caption" } };
    private readonly WrapPanel _selectionBar = new() { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, IsVisible = false };
    private Control? _filters;
    private string? _selectionAnchor;
    private bool _updatingSelection;
    public Func<IReadOnlyList<string>, Task>? MoveSelectionRequested { get; set; }
    public Func<IReadOnlyList<string>, Task>? TrashSelectionRequested { get; set; }

    private void InitializeSelection(Control filters)
    {
        _filters = filters;
        var move = new Button { Content = Icons.Label("IconMove", "Déplacer"), Classes = { "ghost" } };
        var trash = new Button { Content = Icons.Label("IconTrash", "Corbeille"), Classes = { "danger" } };
        var clear = new Button { Content = "Désélectionner", Classes = { "ghost" } };
        AutomationProperties.SetName(move, "Déplacer la sélection");
        AutomationProperties.SetName(trash, "Mettre la sélection à la corbeille");
        AutomationProperties.SetName(clear, "Désélectionner les fichiers");
        AutomationProperties.SetLiveSetting(_selectionLabel, AutomationLiveSetting.Polite);
        move.Click += async (_, _) => { if (MoveSelectionRequested is { } action) await action(_selectedFiles.ToArray()); };
        trash.Click += async (_, _) => { if (TrashSelectionRequested is { } action) await action(_selectedFiles.ToArray()); };
        clear.Click += (_, _) => { ClearSelection(); SearchInput.Focus(); };
        _selectionBar.Children.Add(_selectionLabel); _selectionBar.Children.Add(move); _selectionBar.Children.Add(trash); _selectionBar.Children.Add(clear);
        Children.Add(_selectionBar);
        InitializeFileKeyboard();
    }

    public void SetBatchBusy(bool busy)
    {
        Rows.IsEnabled = _selectionBar.IsEnabled = !busy;
        if (_filters is not null) _filters.IsEnabled = !busy;
    }

    public void ClearSelection()
    {
        _selectedFiles.Clear(); _selectionAnchor = null; UpdateSelection();
    }

    /// <summary>Ctrl toggles a file, Shift selects a range in the current filtered order.</summary>
    public bool SelectFile(string path, KeyModifiers modifiers)
    {
        if (_loading || !Rows.IsEnabled) return false;
        var family = FamilyInput.SelectedItem is FileFamily selected ? selected : FileFamily.All;
        var files = FileCatalogFilter.Filter(_entries, SearchInput.Text, family).Where(e => !e.IsDirectory).Select(e => e.FullPath).ToList();
        var index = files.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        var anchor = _selectionAnchor is null ? -1 : files.FindIndex(p => string.Equals(p, _selectionAnchor, StringComparison.OrdinalIgnoreCase));
        if (modifiers.HasFlag(KeyModifiers.Shift) && anchor >= 0)
        {
            if (!modifiers.HasFlag(KeyModifiers.Control)) _selectedFiles.Clear();
            for (var i = Math.Min(index, anchor); i <= Math.Max(index, anchor); i++) _selectedFiles.Add(files[i]);
        }
        else
        {
            if (!_selectedFiles.Add(path)) _selectedFiles.Remove(path);
            _selectionAnchor = path;
        }
        UpdateSelection(); return true;
    }

    private void UpdateSelection()
    {
        _selectionBar.IsVisible = _selectedFiles.Count > 0;
        _selectionLabel.Text = $"{_selectedFiles.Count} fichier(s) sélectionné(s)";
        _updatingSelection = true;
        try
        {
            foreach (var check in Rows.GetLogicalDescendants().OfType<CheckBox>().Where(c => c.Tag is string))
            {
                var selected = _selectedFiles.Contains((string)check.Tag!); check.IsChecked = selected;
                if (check.Parent?.Parent is Border border) border.Classes.Set("file-selected", selected);
            }
        }
        finally { _updatingSelection = false; }
    }

    private Control SelectionHeader()
    {
        var header = _header(); header.Margin = new Thickness(36, 0, 0, 0); return header;
    }

    private Control SelectionRow(ProjectFileEntry entry)
    {
        var content = _row(entry);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*") };
        var border = new Border { Child = grid, Classes = { "file-selection-row" } };
        Grid.SetColumn(content, 1); grid.Children.Add(content);
        if (!entry.IsDirectory)
        {
            var check = new CheckBox { Tag = entry.FullPath, IsChecked = _selectedFiles.Contains(entry.FullPath), IsEnabled = !_loading,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MinWidth = 32, MinHeight = 40 };
            AutomationProperties.SetName(check, "Sélectionner " + entry.Name);
            if (check.IsChecked == true) border.Classes.Add("file-selected");
            check.IsCheckedChanged += (_, _) =>
            {
                if (_updatingSelection) return;
                if (check.IsChecked == true) _selectedFiles.Add(entry.FullPath); else _selectedFiles.Remove(entry.FullPath);
                _selectionAnchor = entry.FullPath; UpdateSelection();
            };
            grid.Children.Add(check);
            // Only the open button receives these shortcuts; menus and checkboxes keep their native input.
            if (content is Grid row && row.Children.OfType<Button>().FirstOrDefault(b => b.Tag as string == entry.FullPath) is { } open)
            {
                open.AddHandler(PointerPressedEvent, (_, e) =>
                {
                    if (!e.GetCurrentPoint(open).Properties.IsLeftButtonPressed || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == 0) return;
                    SelectFile(entry.FullPath, e.KeyModifiers); e.Handled = true;
                }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                AttachOutgoingFileDrag(open, entry.FullPath);
                open.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Space && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0)
                    { SelectFile(entry.FullPath, e.KeyModifiers); e.Handled = true; }
                };
            }
        }
        return border;
    }
}
