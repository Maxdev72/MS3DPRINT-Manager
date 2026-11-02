using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

internal sealed record JournalPointingItem(Guid Id, string Label, decimal Amount, bool IsCancelled, DateTimeOffset? PointedAt);

/// <summary>Shared manual-check controls for the two annual journals.</summary>
internal sealed class JournalPointingControls
{
    private readonly Dictionary<Guid, JournalPointingItem> _items;
    private readonly HashSet<Guid> _selected = [];
    private readonly List<CheckBox> _selectionChecks = [];
    private readonly int _filter;
    private readonly Func<Guid[], bool, Task> _apply;
    private readonly TextBlock _selectionSummary = new() { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _point = new() { Content = "Pointer la sélection", IsEnabled = false };
    private readonly Button _unpoint = new() { Content = "Dépointer la sélection", IsEnabled = false, Classes = { "ghost" } };
    private bool _updating;

    public JournalPointingControls(IReadOnlyList<JournalPointingItem> items, int filter, Action<int> changeFilter, Func<Guid[], bool, Task> apply)
    {
        _items = items.ToDictionary(item => item.Id); _filter = filter; _apply = apply;
        Filters = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        var names = new[] { "Tous", "À pointer", "Pointés" };
        for (var i = 0; i < names.Length; i++)
        {
            var value = i;
            var button = new Button { Content = names[i], Classes = { "filter-chip" } };
            button.Classes.Set("selected", i == filter);
            AutomationProperties.SetName(button, "Pointage : " + names[i]);
            button.Click += (_, _) => changeFilter(value);
            Filters.Children.Add(button);
        }
        AutomationProperties.SetName(_point, "Pointer la sélection");
        AutomationProperties.SetName(_unpoint, "Dépointer la sélection");
        _point.Click += async (_, _) => await _apply(_selected.Where(id => _items[id].PointedAt is null).ToArray(), true);
        _unpoint.Click += async (_, _) => await _apply(_selected.Where(id => _items[id].PointedAt is not null).ToArray(), false);
        UpdateSelection();
    }

    private WrapPanel Filters { get; }
    public bool Includes(Guid id) => _filter == 0 || (!_items[id].IsCancelled && (_items[id].PointedAt is not null) == (_filter == 2));

    public Control Header(Control tableHeader, Control? warning = null)
    {
        if (tableHeader is Border { Child: Grid table }) table.ColumnSpacing = 12;
        var active = _items.Values.Where(item => !item.IsCancelled).ToArray();
        var checkedItems = active.Where(item => item.PointedAt is not null).ToArray();
        var pending = active.Where(item => item.PointedAt is null).ToArray();
        var summary = new TextBlock
        {
            Text = $"Pointés : {checkedItems.Length} · {RevenueExporter.Money(checkedItems.Sum(i => i.Amount))}   —   À pointer : {pending.Length} · {RevenueExporter.Money(pending.Sum(i => i.Amount))}",
            Classes = { "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        var batch = new WrapPanel { ItemSpacing = 12, LineSpacing = 8, Children = { _selectionSummary, _point, _unpoint } };
        var all = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(all, "Sélectionner toutes les lignes filtrées pour le pointage");
        ToolTip.SetTip(all, "Sélectionner les lignes de ce filtre, sur toutes les pages");
        var eligible = _items.Values.Where(i => Includes(i.Id) && !i.IsCancelled).Select(i => i.Id).ToArray();
        all.IsEnabled = eligible.Length > 0;
        all.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _selected.Clear();
            if (all.IsChecked == true) _selected.UnionWith(eligible);
            UpdateSelection();
        };
        _selectionChecks.Add(all);
        var header = Columns(all, tableHeader, new TextBlock { Text = "POINTÉ", Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 8), Children = { Filters, summary, batch, header } };
        if (warning is not null) body.Children.Insert(0, warning);
        if (!active.Any(i => Includes(i.Id)) && _filter != 0)
            body.Children.Add(new TextBlock { Text = "Aucune ligne dans ce filtre.", Classes = { "caption" } });
        return body;
    }

    public Control Row(Control row, Guid id)
    {
        if (row is Grid table) table.ColumnSpacing = 12;
        var item = _items[id];
        var select = new CheckBox { Tag = id, IsEnabled = !item.IsCancelled, IsChecked = _selected.Contains(id), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(select, "Sélectionner pour le pointage : " + item.Label);
        select.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            if (select.IsChecked == true) _selected.Add(id); else _selected.Remove(id);
            UpdateSelection();
        };
        _selectionChecks.Add(select);
        select.DetachedFromVisualTree += (_, _) => _selectionChecks.Remove(select);
        Control point;
        if (item.IsCancelled) point = new TextBlock { Text = "—", Classes = { "faint" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        else
        {
            var check = new CheckBox { IsChecked = item.PointedAt is not null, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(check, "Pointage de : " + item.Label);
            ToolTip.SetTip(check, item.PointedAt is { } at ? $"Vérifié sur le relevé le {at:dd/MM/yyyy à HH:mm} · Décocher pour dépointer" : "Cocher après vérification sur le relevé bancaire");
            check.IsCheckedChanged += async (_, _) =>
            {
                if (_updating) return;
                var pointed = check.IsChecked == true;
                // Show only the saved state until confirmation and writing have succeeded.
                _updating = true;
                try { check.IsChecked = item.PointedAt is not null; }
                finally { _updating = false; }
                await _apply([id], pointed);
            };
            point = check;
        }
        return Columns(select, row, point);
    }

    private void UpdateSelection()
    {
        _updating = true;
        try
        {
            foreach (var check in _selectionChecks)
                check.IsChecked = check.Tag is Guid id ? _selected.Contains(id)
                    : _selected.Count > 0 && _items.Values.Where(i => Includes(i.Id) && !i.IsCancelled).All(i => _selected.Contains(i.Id));
            var selected = _selected.Select(id => _items[id]).ToArray();
            _selectionSummary.Text = selected.Length == 0 ? "Sélectionnez des lignes pour pointer en lot." : $"{selected.Length} ligne{(selected.Length > 1 ? "s sélectionnées" : " sélectionnée")} · {RevenueExporter.Money(selected.Sum(i => i.Amount))}";
            _point.IsEnabled = selected.Any(i => i.PointedAt is null);
            _unpoint.IsEnabled = selected.Any(i => i.PointedAt is not null);
        }
        finally { _updating = false; }
    }

    private static Grid Columns(Control select, Control content, Control point)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*,72") };
        Grid.SetColumn(content, 1); Grid.SetColumn(point, 2);
        grid.Children.Add(select); grid.Children.Add(content); grid.Children.Add(point);
        return grid;
    }
}
