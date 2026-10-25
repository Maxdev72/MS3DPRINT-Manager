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
    /// <summary>Coloured pill for a project status label (A_FAIRE, EN_COURS or TERMINE).</summary>
    private static Border CreateStatusBadge(string statusLabel)
    {
        var (text, variant) = statusLabel switch
        {
            "EN_COURS" => ("En cours", "info"),
            "TERMINE" => ("Terminé", "success"),
            _ => ("À faire", "neutral")
        };
        return new Border { Classes = { "badge", variant }, Child = new TextBlock { Text = text } };
    }

    /// <summary>Status of a project: a ring filled to its progress, the percentage, then the status pill.</summary>
    private static Control CreateProjectStatusCell(ProjectSummary project)
    {
        var percent = project.ProgressPercent;
        var brushKey = project.StatusLabel switch { "TERMINE" => "SuccessBrush", "EN_COURS" => "AccentBrush", _ => "FaintTextBrush" };
        var brush = (IBrush?)Avalonia.Application.Current?.FindResource(brushKey) ?? Brushes.Gray;
        var track = (IBrush?)Avalonia.Application.Current?.FindResource("BorderBrush") ?? Brushes.DimGray;
        const double size = 18, thickness = 2.5, radius = (size - thickness) / 2, centre = size / 2;

        var ring = new Panel { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        ring.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = size, Height = size, Stroke = track, StrokeThickness = thickness });
        if (percent >= 100)
            ring.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = size, Height = size, Stroke = brush, StrokeThickness = thickness });
        else if (percent > 0)
        {
            var angle = percent / 100.0 * 2 * Math.PI;
            var figure = new PathFigure { StartPoint = new Avalonia.Point(centre, centre - radius), IsClosed = false };
            figure.Segments!.Add(new ArcSegment
            {
                Point = new Avalonia.Point(centre + radius * Math.Sin(angle), centre - radius * Math.Cos(angle)),
                Size = new Avalonia.Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = percent > 50
            });
            var geometry = new PathGeometry();
            geometry.Figures!.Add(figure);
            ring.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Data = geometry,
                Stroke = brush, StrokeThickness = thickness, StrokeLineCap = PenLineCap.Round
            });
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(ring);
        row.Children.Add(new TextBlock { Text = $"{percent} %", MinWidth = 34, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(CreateStatusBadge(project.StatusLabel));
        ToolTip.SetTip(row, $"Avancement : {percent} %");
        return row;
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

    private static Control CreateReadErrorState(string title, string message, Exception exception, Func<Task> retry, bool compact = false)
    {
        var state = (StackPanel)CreateEmptyState("IconAlert", title, message, "Réessayer", async (sender, _) =>
        {
            if (sender is not Button button || !button.IsEnabled) return;
            button.IsEnabled = false;
            try { await retry(); }
            finally { button.IsEnabled = true; }
        });
        state.Margin = new Avalonia.Thickness(16, 24);
        var detail = new SelectableTextBlock { Text = exception.Message, TextWrapping = TextWrapping.Wrap, MaxWidth = 560, FontSize = 13 };
        var detailBox = new Border
        {
            IsVisible = false, Padding = new Avalonia.Thickness(12), CornerRadius = new Avalonia.CornerRadius(4),
            Background = (IBrush?)Avalonia.Application.Current?.FindResource("RaisedSurfaceBrush"),
            Child = detail
        };
        var toggle = new Button { Content = Icons.Label("IconChevronDown", "Afficher le détail technique"), Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(toggle, "Afficher le détail technique");
        toggle.Click += (_, _) =>
        {
            detailBox.IsVisible = !detailBox.IsVisible;
            var label = detailBox.IsVisible ? "Masquer le détail technique" : "Afficher le détail technique";
            toggle.Content = Icons.Label(detailBox.IsVisible ? "IconChevronUp" : "IconChevronDown", label);
            Avalonia.Automation.AutomationProperties.SetName(toggle, label);
        };
        state.Children.Add(toggle); state.Children.Add(detailBox);
        if (compact)
        {
            var icon = state.Children[0]; var heading = (TextBlock)state.Children[1]; var explanation = (TextBlock)state.Children[2]; var retryButton = state.Children[3];
            state.Children.Clear();
            icon.Width = 32; icon.Height = 32;
            if (icon is Border { Child: Control glyph }) { glyph.Width = 18; glyph.Height = 18; }
            heading.HorizontalAlignment = HorizontalAlignment.Left; heading.TextWrapping = TextWrapping.Wrap;
            explanation.HorizontalAlignment = HorizontalAlignment.Stretch; explanation.TextAlignment = TextAlignment.Left; explanation.MaxWidth = double.PositiveInfinity;
            var text = new StackPanel { Spacing = 4, Children = { heading, explanation } };
            toggle.HorizontalAlignment = HorizontalAlignment.Left;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), ColumnSpacing = 12, RowSpacing = 4, Margin = new Avalonia.Thickness(12) };
            Grid.SetColumn(text, 1); Grid.SetColumn(retryButton, 2);
            Grid.SetColumn(toggle, 1); Grid.SetRow(toggle, 1);
            Grid.SetColumn(detailBox, 1); Grid.SetColumnSpan(detailBox, 2); Grid.SetRow(detailBox, 2);
            grid.Children.Add(icon); grid.Children.Add(text); grid.Children.Add(retryButton); grid.Children.Add(toggle); grid.Children.Add(detailBox);
            return grid;
        }
        return state;
    }

    // one look for every list header, sortable or not: the size of the sort buttons (Button.sort-head), the faint colour, the same padding
    private const double HeaderFontSize = 13;

    private static readonly Avalonia.Thickness HeaderPadding = new(14, 12);

    /// <summary>Header whose labels are buttons: click to sort, click again to reverse. The last heading (actions) is not sortable.</summary>
    private static Border CreateSortableHeader(string columns, string[] headings, int active, bool ascending, Action<int> onSort)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 16 };
        for (var index = 0; index < headings.Length; index++)
        {
            Control cell;
            if (index == headings.Length - 1)
                // centred like the actions button below it
                cell = new TextBlock { Text = ReadableColumnHeading(headings[index]), FontSize = HeaderFontSize, FontWeight = FontWeight.SemiBold, Classes = { "faint" }, HorizontalAlignment = HorizontalAlignment.Center };
            else
            {
                var column = index;
                var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                label.Children.Add(new TextBlock { Text = ReadableColumnHeading(headings[index]) });
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
        return new Border { Classes = { "table-head" }, Padding = HeaderPadding, Child = grid };
    }

    private static Border CreateCompactTableHeader(string columns, params string[] headings)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 16 };
        for (var index = 0; index < headings.Length; index++)
        {
            var heading = new TextBlock { Text = ReadableColumnHeading(headings[index]), FontSize = HeaderFontSize, FontWeight = FontWeight.SemiBold, Classes = { "faint" } };
            // the actions button is centred in its column, so is its heading
            if (headings[index] == "ACTIONS") heading.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(heading, index);
            grid.Children.Add(heading);
        }
        return new Border { Classes = { "table-head" }, Padding = HeaderPadding, Child = grid };
    }

    /// <summary>Name with a leading icon, for file and folder rows.</summary>
    private static Control CreateNameCell(string iconKey, string text, bool accentIcon, string? modelPath = null)
    {
        var icon = new Avalonia.Controls.Shapes.Path { Classes = { "icon" }, Width = 17, Height = 17 };
        if (accentIcon) icon.Classes.Add("icon-accent");
        if (Avalonia.Application.Current?.TryFindResource(iconKey, out var geometry) == true && geometry is Geometry data) icon.Data = data;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
        if (modelPath is not null && ThreeDFileSupport.IsPreviewable(modelPath))
        {
            // the icon stands in until the picture of the model is ready
            var picture = new Avalonia.Controls.Image { Stretch = Stretch.UniformToFill };
            var frame = new Border { Width = 40, Height = 40, CornerRadius = new Avalonia.CornerRadius(6), ClipToBounds = true, IsVisible = false, Child = picture };
            var slot = new Panel { Width = 40, Height = 40, Children = { icon, frame } };
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(slot);
            _ = new ThumbnailBinding(row, picture, token => ModelThumbnails.GetAsync(modelPath, token), ready => { frame.IsVisible = ready == true; icon.IsVisible = ready != true; });
        }
        else row.Children.Add(icon);
        var label = new TextBlock { Text = text, FontWeight = accentIcon ? FontWeight.SemiBold : FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1); row.Children.Add(label);
        return row;
    }

    /// <summary>A larger picture of the model when the pointer rests on its row; the picture is made on the first visit and kept.</summary>
    private static void AttachLargePreview(Control row, string modelPath, string title)
    {
        const double size = MS3DPRINT.Manager.Desktop.Viewer.ModelThumbnails.PreviewSize;
        var picture = new Avalonia.Controls.Image { Width = size, Height = size, Stretch = Stretch.Uniform };
        var waiting = new TextBlock { Text = "Aperçu en cours…", Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var frame = new Border { Width = size, Height = size, CornerRadius = new Avalonia.CornerRadius(8), ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x15, 0x22)), Child = new Panel { Children = { waiting, picture } } };
        var caption = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = size };
        // the whole name cell answers, not only the picture and the words
        if (row is Panel panel) panel.Background = Brushes.Transparent;
        row.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTip.SetTip(row, new StackPanel { Spacing = 8, Children = { frame, caption } });
        ToolTip.SetShowDelay(row, 350);
        ToolTip.SetPlacement(row, Avalonia.Controls.PlacementMode.Right);
        var binding = new ThumbnailBinding(row, picture, token => ModelThumbnails.GetAsync(modelPath, token, ModelThumbnails.PreviewSize), ready =>
        {
            waiting.IsVisible = ready != true;
            waiting.Text = ready == false ? "Aperçu indisponible pour ce fichier." : "Aperçu en cours…";
        }, automatic: false);
#if DEBUG
        // development hook (Debug builds only): opens the first preview by itself, to check it without a pointer
        if (Environment.GetEnvironmentVariable("MS3DPRINT_TEST_TIP") == "1")
            Dispatcher.UIThread.Post(() => DispatcherTimer.RunOnce(() => { _ = binding.StartAsync(); ToolTip.SetIsOpen(row, true); }, TimeSpan.FromSeconds(3)));
#endif
        row.PointerEntered += (_, _) => _ = binding.StartAsync();
    }

    private static TextBlock CreateCompactTableCell(string value, bool prominent = false, bool accent = false)
    {
        var cell = new TextBlock
        {
            Text = value,
            FontWeight = prominent ? FontWeight.SemiBold : FontWeight.Normal,
            FontSize = prominent ? 16 : 15,
            TextWrapping = prominent ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxLines = prominent ? 2 : 1,
            LineHeight = 22,
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
        Panel panel => string.Join(" ", panel.Children.Select(CellText).Where(text => text.Length > 0)),
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
        var row = new Grid { Classes = { "workspace-row" }, ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 16, Margin = new Avalonia.Thickness(14, 0), MinHeight = 60 };
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
            cells[index].IsHitTestVisible = open is null || cells[index].Classes.Contains("interactive-cell");
            cells[index].VerticalAlignment = VerticalAlignment.Center;
            // a pill keeps its own width instead of filling the whole column
            if (cells[index] is Border { Classes: var classes } && classes.Contains("badge")) cells[index].HorizontalAlignment = HorizontalAlignment.Left;
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

    /// <summary>Project, client, status, due date, actions.</summary>
    private const string ProjectRowColumns = "3*,1.4*,170,110,84";

    private const string TrashRowColumns = "2*,*,130";

    private const string ClientRowColumns = "2*,80,*,110,64";

    private bool _projectListMode;

    private bool _clientListMode;

    private Button CreateOverflowActions(params Button[] actions)
        => CreateOverflowActions(PlacementMode.BottomEdgeAlignedRight, actions);

    private Button CreateOverflowActions(PlacementMode placement, params Button[] actions)
    {
        var menu = new StackPanel { Spacing = 2, MinWidth = 216 };
        foreach (var action in actions)
        {
            if (action.Classes.Contains("menu-destructive") && menu.Children.Count > 0)
                menu.Children.Add(new Border { Height = 1, Margin = new Avalonia.Thickness(8, 6), Background = (IBrush?)Avalonia.Application.Current?.FindResource("SubtleBorderBrush") });
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
                Background = (IBrush?)Avalonia.Application.Current?.FindResource("SurfaceBrush"),
                BorderBrush = (IBrush?)Avalonia.Application.Current?.FindResource("BorderBrush"),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(8),
                Padding = new Avalonia.Thickness(6),
                Child = menu
            }
        };
        flyout.FlyoutPresenterClasses.Add("action-menu-flyout");
        // Popup roots are separate visual trees: read motion preference from the owning window.
        flyout.Opened += (_, _) => BrandMotion.OpenOverlay((Control)flyout.Content!, MotionReduced);
        foreach (var action in actions) action.Click += (_, _) => flyout.Hide();
        var overflow = new Button { Content = Icons.Glyph("IconMore", 18, dots: true), Classes = { "icon-action" } };
        ToolTip.SetTip(overflow, "Actions");
        Avalonia.Automation.AutomationProperties.SetName(overflow, "Actions");
        overflow.Click += (_, _) => flyout.ShowAt(overflow);
        overflow.Tag = flyout; // lets the row open the same menu on right click
        return overflow;
    }
}
