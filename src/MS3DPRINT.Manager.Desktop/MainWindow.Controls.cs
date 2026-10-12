using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Printers;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Desktop.Dashboard;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Shared building blocks for lists: tables, rows, badges, empty states and action menus.</summary>
public partial class MainWindow
{
    /// <summary>Coloured pill for a project status label (DEVIS, EN_COURS, TERMINE or "À compléter").</summary>
    private static Border CreateStatusBadge(string statusLabel)
    {
        var (text, variant) = statusLabel switch
        {
            "DEVIS" => ("Devis", "warning"),
            "EN_COURS" => ("En cours", "info"),
            "TERMINE" => ("Terminé", "success"),
            _ => (statusLabel, "neutral")
        };
        return new Border { Classes = { "badge", variant }, Child = new TextBlock { Text = text } };
    }

    /// <summary>Friendly empty state: icon, explanation and, when useful, the action that fills the list.</summary>
    private static Control CreateEmptyState(string iconKey, string title, string message, string? actionLabel = null, EventHandler<RoutedEventArgs>? action = null, object? actionTag = null)
    {
        var stack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 36) };
        var icon = new Avalonia.Controls.Shapes.Path { Classes = { "icon", "icon-accent" }, Width = 30, Height = 30 };
        if (Avalonia.Application.Current?.TryFindResource(iconKey, out var geometry) == true && geometry is Geometry data) icon.Data = data;
        stack.Children.Add(new Border { Classes = { "icon-chip" }, Width = 56, Height = 56, CornerRadius = new Avalonia.CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Center, Child = icon });
        stack.Children.Add(new TextBlock { Text = title, Classes = { "card-title" }, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = message, Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
        if (actionLabel is not null && action is not null)
        {
            var button = new Button { Content = actionLabel, Tag = actionTag, Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
            button.Click += action;
            stack.Children.Add(button);
        }
        return stack;
    }

    /// <summary>Header whose labels are buttons: click to sort, click again to reverse. The last heading (actions) is not sortable.</summary>
    private static Border CreateSortableHeader(string columns, string[] headings, int active, bool ascending, Action<int> onSort)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 16 };
        for (var index = 0; index < headings.Length; index++)
        {
            Control cell;
            if (index == headings.Length - 1)
                cell = new TextBlock { Text = headings[index], FontSize = 12, FontWeight = FontWeight.SemiBold, Classes = { "faint" } };
            else
            {
                var column = index;
                var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                label.Children.Add(new TextBlock { Text = headings[index] });
                if (index == active) label.Children.Add(Icons.Glyph(ascending ? "IconChevronUp" : "IconChevronDown", 11));
                var button = new Button { Content = label, Classes = { "sort-head" } };
                if (index == active) button.Classes.Add("active");
                ToolTip.SetTip(button, "Trier par " + headings[index].ToLowerInvariant());
                button.Click += (_, _) => onSort(column);
                cell = button;
            }
            Grid.SetColumn(cell, index);
            grid.Children.Add(cell);
        }
        return new Border { Background = Avalonia.Media.Brushes.Transparent, Padding = new Avalonia.Thickness(14, 4), Child = grid };
    }

    private static Border CreateCompactTableHeader(string columns, params string[] headings)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 16 };
        for (var index = 0; index < headings.Length; index++)
        {
            var heading = new TextBlock { Text = headings[index], FontSize = 12, FontWeight = FontWeight.SemiBold, Classes = { "muted" } };
            Grid.SetColumn(heading, index);
            grid.Children.Add(heading);
        }
        return new Border { Background = Avalonia.Media.Brushes.Transparent, Padding = new Avalonia.Thickness(14, 6), Child = grid };
    }

    /// <summary>Name with a leading icon, for file and folder rows.</summary>
    private static Control CreateNameCell(string iconKey, string text, bool accentIcon, string? modelPath = null)
    {
        var icon = new Avalonia.Controls.Shapes.Path { Classes = { "icon" }, Width = 17, Height = 17 };
        if (accentIcon) icon.Classes.Add("icon-accent");
        if (Avalonia.Application.Current?.TryFindResource(iconKey, out var geometry) == true && geometry is Geometry data) icon.Data = data;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        if (modelPath is not null && MS3DPRINT.Manager.Desktop.Viewer.ModelThumbnails.IsEligible(modelPath))
        {
            // the icon stands in until the picture of the model is ready
            var picture = new Avalonia.Controls.Image { Stretch = Stretch.UniformToFill };
            var frame = new Border { Width = 32, Height = 32, CornerRadius = new Avalonia.CornerRadius(6), ClipToBounds = true, IsVisible = false, Child = picture };
            var slot = new Panel { Width = 32, Height = 32, Children = { icon, frame } };
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(slot);
            _ = ShowThumbnailAsync(modelPath, picture, frame, icon);
        }
        else row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = text, FontWeight = accentIcon ? FontWeight.SemiBold : FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static async Task ShowThumbnailAsync(string modelPath, Avalonia.Controls.Image picture, Control frame, Control icon)
    {
        var bitmap = await MS3DPRINT.Manager.Desktop.Viewer.ModelThumbnails.GetAsync(modelPath);
        if (bitmap is null) return;
        Dispatcher.UIThread.Post(() => { picture.Source = bitmap; frame.IsVisible = true; icon.IsVisible = false; });
    }

    private static TextBlock CreateCompactTableCell(string value, bool prominent = false, bool accent = false)
    {
        var cell = new TextBlock
        {
            Text = value,
            FontWeight = prominent ? FontWeight.SemiBold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        ToolTip.SetTip(cell, value);
        if (accent)
            cell.Foreground = (IBrush?)Avalonia.Application.Current?.FindResource("AccentBrush") ?? Brushes.CornflowerBlue;

        return cell;
    }

    private static string CellText(Control cell) => cell switch
    {
        TextBlock text => text.Text ?? string.Empty,
        Panel panel => string.Join(" ", panel.Children.OfType<TextBlock>().Select(text => text.Text)),
        _ => string.Empty
    };

    private const string ActionRowColumns = "2*,*,110,64";

    /// <summary>
    /// Table row opened by clicking it, with a separate actions menu on the right.
    /// The row button sits behind the cells (which ignore the pointer) so the menu is never nested in it.
    /// </summary>
    private static Control CreateActionRow(Button? open, Button? actions, params Control[] cells)
        => CreateActionRow(ActionRowColumns, open, actions, cells);

    private static Control CreateActionRow(string columns, Button? open, Button? actions, params Control[] cells)
    {
        var columnCount = columns.Split(',').Length;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 16, Margin = new Avalonia.Thickness(14, 0), MinHeight = 46 };
        if (open is not null)
        {
            open.Classes.Add("table-row");
            if (cells.Length > 0 && CellText(cells[0]) is { Length: > 0 } rowName)
                Avalonia.Automation.AutomationProperties.SetName(open, "Ouvrir " + rowName);
            open.Margin = new Avalonia.Thickness(-14, 0);
            Grid.SetColumnSpan(open, columnCount);
            row.Children.Add(open);
        }
        for (var index = 0; index < cells.Length; index++)
        {
            cells[index].IsHitTestVisible = open is null;
            cells[index].VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cells[index], index);
            row.Children.Add(cells[index]);
        }
        if (actions?.Tag is Flyout contextMenu)
            row.ContextRequested += (_, e) => { contextMenu.ShowAt(row, showAtPointer: true); e.Handled = true; };
        if (actions is not null)
        {
            actions.HorizontalAlignment = HorizontalAlignment.Center;
            actions.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(actions, columnCount - 1);
            row.Children.Add(actions);
        }
        if (open is not null) return row;
        return new Border { BorderBrush = (IBrush?)Avalonia.Application.Current?.FindResource("SubtleBorderBrush"), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Child = row };
    }

    private const string TrashRowColumns = "2*,*,130";

    private const string ClientRowColumns = "2*,80,*,110,64";

    private bool _projectListMode;

    private bool _clientListMode;

    private static Button CreateOverflowActions(params Button[] actions)
        => CreateOverflowActions(PlacementMode.BottomEdgeAlignedRight, actions);

    private static Button CreateOverflowActions(PlacementMode placement, params Button[] actions)
    {
        var menu = new StackPanel { Spacing = 2, MinWidth = 184 };
        foreach (var action in actions)
        {
            action.Classes.Add("menu-action");
            action.HorizontalAlignment = HorizontalAlignment.Stretch;
            action.HorizontalContentAlignment = HorizontalAlignment.Left;
            menu.Children.Add(action);
        }

        var flyout = new Flyout
        {
            Placement = placement,
            Content = new Border
            {
                Background = (IBrush?)Avalonia.Application.Current?.FindResource("RaisedSurfaceBrush"),
                BorderBrush = (IBrush?)Avalonia.Application.Current?.FindResource("BorderBrush"),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(10),
                Padding = new Avalonia.Thickness(6),
                Child = menu
            }
        };
        flyout.FlyoutPresenterClasses.Add("action-menu-flyout");
        var overflow = new Button { Content = Icons.Glyph("IconMore", 18, dots: true), Classes = { "icon-action" } };
        ToolTip.SetTip(overflow, "Actions");
        Avalonia.Automation.AutomationProperties.SetName(overflow, "Actions");
        overflow.Click += (_, _) => flyout.ShowAt(overflow);
        overflow.Tag = flyout; // lets the row open the same menu on right click
        return overflow;
    }
}
