using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using System.Runtime.CompilerServices;
using Avalonia.Threading;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Bounded control creation. Filtering and ordering happen before paging.</summary>
public static class PagedRows
{
    public const int PageSize = 50;
    private sealed class Position { public object? Scope; public int Page; }
    private static readonly ConditionalWeakTable<StackPanel, Position> Positions = new();

    public static int GetPage(StackPanel host) => Positions.TryGetValue(host, out var position) ? position.Page : 0;

    public static void Render<T>(StackPanel host, IReadOnlyList<T> items, Func<T, Control> row, Func<Control> header, object? scope = null, int? restorePage = null)
    {
        var position = Positions.GetOrCreateValue(host);
        var lastPage = Math.Max(0, (items.Count - 1) / PageSize);
        var page = restorePage is { } saved ? Math.Clamp(saved, 0, lastPage) : scope is not null && Equals(position.Scope, scope) ? Math.Min(position.Page, lastPage) : 0;
        position.Scope = scope;
        void Show()
        {
            position.Page = page;
            host.Children.Clear();
            if (items.Count > PageSize) host.Children.Add(Navigation());
            host.Children.Add(header());
            var end = Math.Min(items.Count, (page + 1) * PageSize);
            for (var i = page * PageSize; i < end; i++) host.Children.Add(row(items[i]));
            if (items.Count > PageSize) host.Children.Add(Navigation());
        }
        Control Navigation()
        {
            var previous = new Button { Content = Icons.Label("IconArrowLeft", "Précédent", 14), IsEnabled = page > 0, Classes = { "ghost" } };
            var next = new Button { Content = Icons.Label("IconArrowRight", "Suivant", 14), IsEnabled = (page + 1) * PageSize < items.Count, Classes = { "ghost" } };
            Avalonia.Automation.AutomationProperties.SetName(previous, "Page précédente");
            Avalonia.Automation.AutomationProperties.SetName(next, "Page suivante");
            void Move(int delta)
            {
                page = Math.Clamp(page + delta, 0, lastPage);
                Show();
                if (host.GetLogicalAncestors().OfType<ScrollViewer>().FirstOrDefault() is { } scroll) scroll.Offset = default;
                var buttons = host.Children[0].GetLogicalDescendants().OfType<Button>().Where(button => button.IsEnabled).ToArray();
                var action = delta > 0 ? "Page suivante" : "Page précédente";
                var target = buttons.FirstOrDefault(button => Avalonia.Automation.AutomationProperties.GetName(button) == action) ?? buttons.FirstOrDefault();
                Dispatcher.UIThread.Post(() =>
                {
                    if (target is { IsEffectivelyVisible: true, IsEnabled: true } && TopLevel.GetTopLevel(target) is not null) target.Focus();
                }, DispatcherPriority.Loaded);
            }
            previous.Click += (_, _) => Move(-1);
            next.Click += (_, _) => Move(1);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { previous, next } };
            var label = new TextBlock { Text = $"{page * PageSize + 1}–{Math.Min(items.Count, (page + 1) * PageSize)} sur {items.Count}", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
            var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, Margin = new Thickness(0, 10) };
            Grid.SetColumn(controls, 1); bar.Children.Add(label); bar.Children.Add(controls);
            return bar;
        }
        Show();
    }
}
