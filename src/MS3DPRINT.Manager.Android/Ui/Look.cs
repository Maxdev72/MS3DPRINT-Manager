using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Companion.Ui;

/// <summary>The visual vocabulary of the app: section cards, choice chips, stat tiles, the brand mark and the gradient hero.</summary>
public static class Look
{
    public static readonly IBrush HeroGradient = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#2459F0"), 0), new GradientStop(Color.Parse("#0C2F7C"), 1) }
    };

    /// <summary>A card with a small heading, to cut a long form into steps the eye can follow.</summary>
    public static Border Section(string title, params Control[] content)
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Pal.Faint, LetterSpacing = 1.2 });
        foreach (var control in content) stack.Children.Add(control);
        return Ui.Card(stack, padding: 18);
    }

    /// <summary>Single-choice pills that wrap onto several lines: quicker to tap than a drop-down list.</summary>
    public static (Control View, Func<int> Selected, Action<int> Select) Chips(IReadOnlyList<string> labels, Action<int> changed, int initial = 0)
    {
        var buttons = new List<Button>();
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        var selected = initial;
        void Paint()
        {
            for (var i = 0; i < buttons.Count; i++)
            {
                var on = i == selected;
                buttons[i].Background = on ? Pal.AccentSoft : Pal.Raised;
                buttons[i].BorderBrush = on ? Pal.Accent : Pal.Border;
                ((TextBlock)buttons[i].Content!).Foreground = on ? Pal.Accent : Pal.Muted;
            }
        }
        for (var i = 0; i < labels.Count; i++)
        {
            var index = i;
            var chip = new Button
            {
                Content = new TextBlock { Text = labels[i], FontSize = 15, FontWeight = FontWeight.SemiBold }, MinHeight = 44, CornerRadius = new CornerRadius(22), Padding = new Thickness(18, 8),
                BorderThickness = new Thickness(1.5), VerticalContentAlignment = VerticalAlignment.Center
            };
            chip.Click += (_, _) => { selected = index; Paint(); changed(index); };
            buttons.Add(chip); panel.Children.Add(chip);
        }
        Paint();
        return (panel, () => selected, i => { selected = Math.Clamp(i, 0, buttons.Count - 1); Paint(); });
    }

    public static Border Tile(string label, string value, IBrush? valueBrush = null) => new()
    {
        Background = Pal.Surface, BorderBrush = Pal.Subtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12),
        Child = new StackPanel { Spacing = 2, Children =
        {
            new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = valueBrush ?? Pal.Text, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = label, FontSize = 12, Foreground = Pal.Faint, TextWrapping = TextWrapping.Wrap }
        } }
    };

    public static Control Dot(IBrush brush, double size = 9) => new Ellipse { Width = size, Height = size, Fill = brush, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The MS3DPRINT cube, drawn on the same 256-unit grid as the application icon.</summary>
    public static Control Logo(double size)
    {
        static Polygon Face(string color, params (double X, double Y)[] points) => new() { Fill = Brush.Parse(color), Points = [.. points.Select(p => new Point(p.X, p.Y))] };
        var canvas = new Canvas { Width = 256, Height = 256 };
        canvas.Children.Add(Face("#90CAF9", (128, 46), (208, 90), (128, 136), (48, 90)));
        canvas.Children.Add(Face("#2196F3", (48, 90), (128, 136), (128, 218), (48, 172)));
        canvas.Children.Add(Face("#1565C0", (128, 136), (208, 90), (208, 172), (128, 218)));
        foreach (var line in new[] { ((48.0, 90.0), (128.0, 136.0)), ((128.0, 136.0), (208.0, 90.0)), ((128.0, 136.0), (128.0, 218.0)) })
            canvas.Children.Add(new Line { StartPoint = new Point(line.Item1.Item1, line.Item1.Item2), EndPoint = new Point(line.Item2.Item1, line.Item2.Item2), Stroke = Pal.Canvas, StrokeThickness = 6, StrokeLineCap = PenLineCap.Round });
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    /// <summary>"Aujourd'hui", "Hier" or the date, for grouping lists by day.</summary>
    public static string DayLabel(DateOnly day)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (day == today) return "Aujourd’hui";
        if (day == today.AddDays(-1)) return "Hier";
        return day.ToString("dddd d MMMM", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
    }
}
