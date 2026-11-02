using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    private static Color Composite(Color foreground, Color background, double opacity)
    {
        var alpha = foreground.A / 255d * opacity;
        return Color.FromRgb((byte)Math.Round(foreground.R * alpha + background.R * (1 - alpha)),
            (byte)Math.Round(foreground.G * alpha + background.G * (1 - alpha)), (byte)Math.Round(foreground.B * alpha + background.B * (1 - alpha)));
    }

    [Fact]
    public Task Unavailable_actions_remain_legible_without_fading_the_icon_twice() => Run(() =>
    {
        var action = new Button { Content = Icons.Label("IconEdit", "Indisponible"), IsEnabled = false, Classes = { "ghost" } };
        var tool = new Button { Content = Icons.Glyph("IconEdit"), IsEnabled = false, Classes = { "tool" } };
        var input = new TextBox { Text = "Valeur conservée", IsEnabled = false };
        var surface = (ISolidColorBrush)Application.Current!.FindResource("SurfaceBrush")!;
        var window = new Window { Width = 600, Height = 300, Background = surface, Content = new StackPanel { Children = { action, tool, input } } }; window.Show();
        try
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var label = action.GetVisualDescendants().OfType<TextBlock>().Single();
            var labelColor = Composite(((ISolidColorBrush)label.Foreground!).Color, surface.Color, action.Opacity * label.Opacity);
            Assert.True(Contrast(labelColor, surface.Color) >= 4.5);
            var icon = tool.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
            var iconColor = Composite(((ISolidColorBrush)icon.Stroke!).Color, surface.Color, tool.Opacity * icon.Opacity);
            Assert.True(Contrast(iconColor, surface.Color) >= 3);
            Assert.False(action.IsEnabled); Assert.False(tool.IsEnabled);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    private static double Contrast(Color first, Color second)
    {
        static double Linear(byte value) { var channel = value / 255d; return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4); }
        static double Luminance(Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    [Fact]
    public Task Shared_actions_are_borderless_and_readable_including_hover() => Run(async () =>
    {
        static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
        var surface = (IBrush)Application.Current!.FindResource("SurfaceBrush")!;
        var controls = new StackPanel { Spacing = 8, Margin = new Thickness(24) };
        var buttons = new List<Button>();
        foreach (var kind in new[] { "", "ghost", "primary-action", "danger", "filter-chip", "settings-tab", "icon-action", "tool" })
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*,*"), ColumnSpacing = 16 };
            row.Children.Add(new TextBlock { Text = kind.Length == 0 ? "Action secondaire" : kind, VerticalAlignment = VerticalAlignment.Center });
            for (var column = 1; column <= 2; column++)
            {
                var iconOnly = kind is "icon-action" or "tool";
                var button = new Button { Content = iconOnly ? Icons.Glyph("IconEdit") : column == 1 ? "Action disponible" : Icons.Label("IconEdit", "Action avec icône"), HorizontalAlignment = HorizontalAlignment.Left };
                if (kind.Length > 0) button.Classes.Add(kind);
                Grid.SetColumn(button, column); row.Children.Add(button); buttons.Add(button);
            }
            controls.Children.Add(row);
        }
        var tabs = new StackPanel { Name = "ProjectViewTabs", Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var selected in new[] { false, true })
        {
            var button = new Button { Content = selected ? "Vue active" : "Autre vue", Classes = { "filter-chip" } };
            button.Classes.Set("selected", selected); tabs.Children.Add(button); buttons.Add(button);
        }
        controls.Children.Add(tabs);
        var input = new TextBox { Text = "Champ modifiable" }; controls.Children.Add(input);
        var combo = new ComboBox { ItemsSource = new[] { "Choix disponible" }, SelectedIndex = 0 }; controls.Children.Add(combo);
        var window = new Window { Width = 1100, Height = 850, Background = surface, Content = controls, Classes = { "workbench-ui", "reduce-motion" } }; window.Show();
        try
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            foreach (var button in buttons)
            {
                foreach (var hover in new[] { false, true })
                {
                    window.MouseMove(hover ? button.TranslatePoint(new Point(12, 12), window)!.Value : new Point(1, 1));
                    await Task.Delay(180); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
                    var fill = ColorOf(presenter.Background ?? button.Background);
                    if (fill.A == 0) fill = ColorOf(surface);
                    Assert.Equal(new Thickness(0), button.BorderThickness);
                    if (button.Classes.Contains("primary-action"))
                        Assert.True(Contrast(fill, ColorOf(surface)) >= 3);
                    foreach (var text in button.GetVisualDescendants().OfType<TextBlock>())
                        Assert.True(Contrast(ColorOf(text.Foreground), fill) >= 4.5, "Libellé peu lisible : " + string.Join(" ", button.Classes) + " hover=" + hover);
                    foreach (var icon in button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>())
                        Assert.True(Contrast(ColorOf(icon.Stroke), fill) >= 3);
                }
            }
            Assert.True(Contrast(ColorOf(input.BorderBrush), ColorOf(input.Background)) >= 3);
            Assert.True(Contrast(ColorOf(combo.BorderBrush), ColorOf(combo.Background)) >= 3);
            window.MouseMove(new Point(1, 1));
            CaptureDraftUi(window, "shared-actions-contrast");
            foreach (var button in buttons)
            {
                button.Focus(Avalonia.Input.NavigationMethod.Tab);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(new Thickness(2), button.BorderThickness);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Text_and_icons_keep_their_action_color_and_secondary_text_stays_readable() => Run(() =>
    {
        var danger = new Button { Content = Icons.Label("IconTrash", "Supprimer"), Classes = { "danger" } };
        var surface = (IBrush)Application.Current!.FindResource("PressedBrush")!;
        var faint = new TextBlock { Text = "Information secondaire", Classes = { "faint" } };
        var window = new Window { Width = 500, Height = 260, Background = surface, Content = new StackPanel { Children = { danger, faint } } }; window.Show();
        try
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var label = danger.GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(((ISolidColorBrush)danger.Foreground!).Color, ((ISolidColorBrush)label.Foreground!).Color);
            Assert.True(Contrast(((ISolidColorBrush)faint.Foreground!).Color, ((ISolidColorBrush)surface).Color) >= 4.5);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });
}
