using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;

namespace MS3DPRINT.Manager.App.Controls;

public static class CompactTable
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(TableState), typeof(CompactTable));
    private static readonly DependencyProperty ObservedSourceProperty = DependencyProperty.RegisterAttached("ObservedSource", typeof(object), typeof(CompactTable),
        new PropertyMetadata(null, (owner, _) => { if (owner is ListView list && list.GetValue(StateProperty) is TableState state) ApplySort(list, state); }));
    private static readonly DependencyProperty OpenProperty = DependencyProperty.RegisterAttached("Open", typeof(Action), typeof(CompactTable));
    private static readonly DependencyProperty SortPathProperty = DependencyProperty.RegisterAttached("SortPath", typeof(string), typeof(CompactTable));

    public static void Configure(ListView list)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.SelectionMode = SelectionMode.Single;
        list.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        list.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        list.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        ScrollViewer.SetCanContentScroll(list, true);
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        var rows = new Style(typeof(ListViewItem));
        var material = list.TryFindResource("MaterialDesignGridViewItem") as Style;
        if (material is not null) rows.BasedOn = material;
        rows.Setters.Add(new Setter(FrameworkElement.HeightProperty, 38d));
        rows.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        rows.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        rows.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        rows.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        rows.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("BorderBrush")));
        rows.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("CardHoverBrush")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("AccentBrush")));
        rows.Triggers.Add(selected);
        list.ItemContainerStyle = rows;
        if (list.GetValue(StateProperty) is not TableState state)
        {
            state = new TableState();
            list.SetValue(StateProperty, state);
            list.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, args) =>
            {
                if (args.OriginalSource is not GridViewColumnHeader { Column: { } column } || column.GetValue(SortPathProperty) is not string path) return;
                state.Direction = state.Path == path && state.Direction == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
                state.Path = path;
                ApplySort(list, state);
                if (list.View is GridView view)
                    foreach (var item in view.Columns)
                        if (state.Headers.TryGetValue(item, out var label)) item.Header = label + (item == column ? state.Direction == ListSortDirection.Ascending ? " ▲" : " ▼" : "");
                args.Handled = true;
            }));
            BindingOperations.SetBinding(list, ObservedSourceProperty, new Binding(nameof(ItemsControl.ItemsSource)) { Source = list });
        }
        if (list.View is not GridView grid) return;
        grid.AllowsColumnReorder = true;
        var header = new Style(typeof(GridViewColumnHeader));
        if (list.TryFindResource(typeof(GridViewColumnHeader)) is Style headerBase) header.BasedOn = headerBase;
        header.Setters.Add(new Setter(FrameworkElement.HeightProperty, 36d));
        header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 0, 10, 0)));
        header.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        header.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        header.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("SurfaceBrush")));
        header.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("BorderBrush")));
        grid.ColumnHeaderContainerStyle = header;
        foreach (var column in grid.Columns)
        {
            if (column.DisplayMemberBinding is not Binding binding || binding.Path?.Path is not string path) continue;
            column.SetValue(SortPathProperty, path);
            if (column.Header is string label) state.Headers[column] = label;
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            text.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 10, 0));
            text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            text.SetValue(FrameworkElement.LanguageProperty, XmlLanguage.GetLanguage("fr-FR"));
            var display = new Binding(path)
            {
                Converter = binding.Converter ?? new ReadableValueConverter(), ConverterParameter = binding.ConverterParameter,
                ConverterCulture = binding.ConverterCulture ?? CultureInfo.GetCultureInfo("fr-FR"), StringFormat = binding.StringFormat,
                TargetNullValue = binding.TargetNullValue == DependencyProperty.UnsetValue ? "—" : binding.TargetNullValue
            };
            text.SetBinding(TextBlock.TextProperty, display);
            text.SetBinding(FrameworkElement.ToolTipProperty, new Binding(path) { Converter = display.Converter, ConverterCulture = display.ConverterCulture, StringFormat = display.StringFormat });
            column.DisplayMemberBinding = null;
            column.CellTemplate = new DataTemplate { VisualTree = text };
        }
        ApplySort(list, state);
    }

    public static void BindOpen(ListView list, Action open)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(open);
        var attached = list.ReadLocalValue(OpenProperty) != DependencyProperty.UnsetValue;
        list.SetValue(OpenProperty, open);
        if (attached) return;
        list.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter || list.SelectedItem is null || args.OriginalSource is GridViewColumnHeader) return;
            ((Action)list.GetValue(OpenProperty))();
            args.Handled = true;
        };
        list.MouseDoubleClick += (_, args) =>
        {
            if (args.ChangedButton != MouseButton.Left || args.OriginalSource is not DependencyObject origin
                || ItemsControl.ContainerFromElement(list, origin) is not ListViewItem row) return;
            list.SelectedItem = row.Content;
            ((Action)list.GetValue(OpenProperty))();
            args.Handled = true;
        };
    }

    private static void ApplySort(ListView list, TableState state)
    {
        if (state.Path is null || !list.Items.CanSort) return;
        var selection = list.SelectedItem;
        using (list.Items.DeferRefresh())
        {
            list.Items.SortDescriptions.Clear();
            list.Items.SortDescriptions.Add(new SortDescription(state.Path, state.Direction));
        }
        if (selection is not null && list.Items.Contains(selection)) list.SelectedItem = selection;
    }

    private sealed class TableState
    {
        public string? Path { get; set; }
        public ListSortDirection Direction { get; set; }
        public Dictionary<GridViewColumn, string> Headers { get; } = new();
    }

    private sealed class ReadableValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            bool flag => flag ? "Oui" : "Non",
            Core.Search.GlobalSearchResultKind kind => kind switch { Core.Search.GlobalSearchResultKind.Project => "Projet", Core.Search.GlobalSearchResultKind.File => "Fichier", _ => "Client" },
            Core.Clients.ClientKind kind => kind == Core.Clients.ClientKind.Professional ? "Professionnel" : "Particulier",
            _ => value
        };
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
