using System.Windows;
using System.Windows.Controls;

namespace MS3DPRINT.Manager.App.Controls;

public sealed class ResponsiveCardPanel : Panel
{
    public int MaxColumns { get; set; } = 4;
    public double MinimumItemWidth { get; set; } = 240;
    public double MinimumItemHeight { get; set; } = 130;
    public double ItemHeightRatio { get; set; } = 0.42;
    public double Gap { get; set; } = 14;

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = VisibleChildren();
        if (children.Length == 0) return new Size();

        var layout = GetLayout(availableSize.Width, children.Length);
        foreach (var child in children) child.Measure(new Size(layout.ItemWidth, layout.ItemHeight));

        return new Size(layout.PanelWidth, layout.RowCount * layout.ItemHeight + (layout.RowCount - 1) * Gap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = VisibleChildren();
        if (children.Length == 0) return finalSize;

        var layout = GetLayout(finalSize.Width, children.Length);
        for (var index = 0; index < children.Length; index++)
        {
            var row = index / layout.ColumnCount;
            var column = index % layout.ColumnCount;
            children[index].Arrange(new Rect(
                column * (layout.ItemWidth + Gap),
                row * (layout.ItemHeight + Gap),
                layout.ItemWidth,
                layout.ItemHeight));
        }

        return finalSize;
    }

    private UIElement[] VisibleChildren() => InternalChildren.Cast<UIElement>().Where(child => child.Visibility != Visibility.Collapsed).ToArray();

    private Layout GetLayout(double availableWidth, int itemCount)
    {
        var panelWidth = double.IsInfinity(availableWidth)
            ? MinimumItemWidth * MaxColumns + Gap * (MaxColumns - 1)
            : Math.Max(0, availableWidth);
        var columnCount = Math.Clamp((int)Math.Floor((panelWidth + Gap) / (MinimumItemWidth + Gap)), 1, Math.Max(1, MaxColumns));
        var itemWidth = Math.Max(0, (panelWidth - Gap * (columnCount - 1)) / columnCount);
        var itemHeight = Math.Max(MinimumItemHeight, itemWidth * ItemHeightRatio);
        return new Layout(panelWidth, columnCount, itemWidth, itemHeight, (int)Math.Ceiling((double)itemCount / columnCount));
    }

    private sealed record Layout(double PanelWidth, int ColumnCount, double ItemWidth, double ItemHeight, int RowCount);
}
