using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Table_row_hover_covers_the_full_width_including_gutters_and_actions() => Run(async () =>
    {
        var open = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        var actions = new Button { Content = "⋯", Classes = { "icon-action" } };
        var factory = typeof(MainWindow).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(m => m.Name == "CreateActionRow" && m.GetParameters()[0].ParameterType == typeof(string));
        var row = (Grid)factory.Invoke(null, new object?[] { "*,90,90,64", open, actions,
            new Control[] { new TextBlock { Text = "00_BRIEF_CLIENT" }, new TextBlock { Text = "Dossier" }, new TextBlock { Text = "—" } } })!;
        var window = new Window { Width = 600, Height = 160, Classes = { "workbench-ui" }, Content = row };
        window.Show();
        try
        {
            foreach (var width in new[] { 600d, 1300d })
            {
                window.Width = width;
                window.UpdateLayout();
                foreach (var target in new Control[] { open, actions })
                {
                    var point = target == open ? new Point(3, target.Bounds.Height / 2) : new Point(target.Bounds.Width / 2, target.Bounds.Height / 2);
                    window.MouseMove(target.TranslatePoint(point, window)!.Value);
                    await Task.Delay(180);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(row.IsPointerOver);
                    var expectedLeft = open.TranslatePoint(default, window)!.Value.X;
                    var expectedRight = expectedLeft + open.Bounds.Width;
                    var hoverColor = ((ISolidColorBrush)Application.Current!.FindResource("RaisedSurfaceBrush")!).Color;
                    Assert.Contains(row.GetVisualDescendants().Prepend(row).OfType<Control>(), control =>
                    {
                        var background = control switch { Panel p => p.Background, Border b => b.Background, _ => null };
                        if (background is not ISolidColorBrush brush || brush.Color != hoverColor) return false;
                        var left = control.TranslatePoint(default, window)!.Value.X;
                        return Math.Abs(left - expectedLeft) < 0.1 && Math.Abs(left + control.Bounds.Width - expectedRight) < 0.1;
                    });
                    Assert.True(actions.IsEffectivelyEnabled);
                }
                CaptureDraftUi(window, "table-row-hover-" + width);
            }
        }
        finally { window.Close(); }
    });
}
