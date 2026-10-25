using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Companion.Ui;

/// <summary>Same palette as the Windows software, so the two feel like one product.</summary>
public static class Pal
{
    public static readonly IBrush Canvas = Brush.Parse("#071225");
    public static readonly IBrush Sidebar = Brush.Parse("#060E1C");
    public static readonly IBrush Surface = Brush.Parse("#0C1A2D");
    public static readonly IBrush Raised = Brush.Parse("#11243B");
    public static readonly IBrush Hover = Brush.Parse("#183650");
    public static readonly IBrush Border = Brush.Parse("#2A4664");
    public static readonly IBrush Subtle = Brush.Parse("#1C334E");
    public static readonly IBrush Text = Brush.Parse("#F5F9FF");
    public static readonly IBrush Muted = Brush.Parse("#B1C2D9");
    public static readonly IBrush Faint = Brush.Parse("#91AAC8");
    public static readonly IBrush Accent = Brush.Parse("#76B8FF");
    public static readonly IBrush AccentSoft = Brush.Parse("#142C59");
    public static readonly IBrush Brand = Brush.Parse("#245CFF");
    public static readonly IBrush Success = Brush.Parse("#46D6A4");
    public static readonly IBrush SuccessSoft = Brush.Parse("#10322A");
    public static readonly IBrush Warning = Brush.Parse("#F4BC52");
    public static readonly IBrush WarningSoft = Brush.Parse("#392D12");
    public static readonly IBrush Danger = Brush.Parse("#F57F7F");
    public static readonly IBrush DangerSoft = Brush.Parse("#3A1A1F");
    public static readonly IBrush Neutral = Brush.Parse("#1D283B");
}

/// <summary>Icon outlines shared with the Windows software (24 x 24 grid).</summary>
public static class Glyph
{
    public const string Plus = "M12 5v14 M5 12h14";
    public const string Projects = "M3 7.5A1.5 1.5 0 0 1 4.5 6h4.2l2 2.2h8.8A1.5 1.5 0 0 1 21 9.7v7.8a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5Z";
    public const string Activity = "M3 12h4l3-7 4 14 3-7h4";
    public const string Upload = "M12 16V4 M7 9l5-5 5 5 M5 20h14";
    public const string Settings = "M4 7h9 M17 7h3 M15 5v4 M4 17h3 M11 17h9 M9 15v4 M4 12h16";
    public const string Refresh = "M20 12a8 8 0 1 1-2.3-5.7 M20 4v4.5h-4.5";
    public const string Camera = "M3.5 8h3.2l1.6-2.2h7.4L17.3 8h3.2v11h-17Z M8.5 13.5a3.5 3.5 0 1 0 7 0a3.5 3.5 0 1 0-7 0";
    public const string Image = "M4 5h16v14H4Z M4 16l5-5 4 4 3-3 4 4 M9 9.5h.01";
    public const string Check = "M5 12.5l4.5 4.5L19 7.5";
    public const string Alert = "M12 4 21.5 20h-19Z M12 10v4.5 M12 17.4h.01";
    public const string Info = "M12 3.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17Z M12 11v5.5 M12 7.6h.01";
    public const string Close = "M6 6l12 12 M18 6 6 18";
    public const string Search = "M10.5 3a7.5 7.5 0 1 0 0 15 7.5 7.5 0 0 0 0-15Z M16 16l5 5";
    public const string Back = "M15 5l-7 7 7 7";
    public const string Edit = "M4 20l4-1 11-11a2.1 2.1 0 0 0-3-3L5 16Z M14 7l3 3";
    public const string Folder = "M3 7.5A1.5 1.5 0 0 1 4.5 6h4.2l2 2.2h8.8A1.5 1.5 0 0 1 21 9.7v7.8a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5Z";
    public const string Send = "M4 12l16-8-6 16-3-7Z";
    public const string Trash = "M4 7h16 M9.5 7V4.5h5V7 M6 7l1 13h10l1-13 M10 11v6 M14 11v6";
    public const string Clock = "M12 3.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17Z M12 7.5V12l3 2";

    /// <summary>Outlines are drawn as strokes on a 24 x 24 grid, like in the Windows software (a fill would hide open lines).</summary>
    public static Control Icon(string data, double size = 22, IBrush? brush = null)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(data), Stroke = brush ?? Pal.Muted, StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Width = 24, Height = 24, Stretch = Stretch.None
        };
        return new Viewbox { Width = size, Height = size, Child = path };
    }

    public static void Paint(Control icon, IBrush brush)
    {
        if (icon is Viewbox { Child: Avalonia.Controls.Shapes.Path path }) path.Stroke = brush;
    }
}

/// <summary>Small builders for the controls repeated across the screens.</summary>
public static class Ui
{
    public const double Touch = 52;

    public static TextBlock Text(string text, double size = 16, IBrush? brush = null, bool bold = false, bool wrap = true) =>
        new() { Text = text, FontSize = size, Foreground = brush ?? Pal.Text, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };

    public static TextBlock Muted(string text, double size = 14) => Text(text, size, Pal.Muted);
    public static TextBlock Title(string text) => Text(text, 26, Pal.Text, bold: true);
    public static TextBlock Section(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Pal.Text, Margin = new Thickness(0, 10, 0, 0) };

    public static StackPanel Field(string label, Control input, string? hint = null)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Pal.Muted });
        stack.Children.Add(input);
        if (hint is not null) stack.Children.Add(new TextBlock { Text = hint, FontSize = 12, Foreground = Pal.Faint, TextWrapping = TextWrapping.Wrap });
        return stack;
    }

    /// <summary>Raised dark input with a comfortable touch height.</summary>
    public static T Input<T>(T control) where T : Control
    {
        control.MinHeight = Touch;
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (control is TemplatedControl templated)
        {
            templated.Background = Pal.Raised; templated.BorderBrush = Pal.Border; templated.BorderThickness = new Thickness(1);
            templated.CornerRadius = new CornerRadius(12); templated.FontSize = 16; templated.Foreground = Pal.Text;
            templated.Padding = new Thickness(14, 12);
        }
        return control;
    }

    public static Button Primary(string text, string? icon = null) => Make(text, icon, Pal.Brand, Brushes.White, null);
    public static Button Secondary(string text, string? icon = null) => Make(text, icon, Pal.Raised, Pal.Text, Pal.Border);
    public static Button Quiet(string text, string? icon = null) => Make(text, icon, Brushes.Transparent, Pal.Accent, null);
    public static Button Danger(string text, string? icon = null) => Make(text, icon, Pal.DangerSoft, Pal.Danger, null);

    private static Button Make(string text, string? icon, IBrush background, IBrush foreground, IBrush? border)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null) row.Children.Add(Glyph.Icon(icon, 20, foreground));
        row.Children.Add(new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = foreground, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        return new Button
        {
            Content = row, MinHeight = Touch, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Background = background, Foreground = foreground, BorderBrush = border, BorderThickness = new Thickness(border is null ? 0 : 1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 10)
        };
    }

    public static Border Card(Control content, IBrush? background = null, IBrush? border = null, double padding = 16) => new()
    {
        Background = background ?? Pal.Surface, BorderBrush = border ?? Pal.Subtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(padding), Child = content
    };

    public static Border Pill(string text, IBrush background, IBrush foreground) => new()
    {
        Background = background, CornerRadius = new CornerRadius(20), Padding = new Thickness(10, 3), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = foreground }
    };

    /// <summary>A banner for a result: green for success, amber for a warning, red for an error.</summary>
    public static Border Banner(string message, BannerKind kind)
    {
        var (background, foreground, icon) = kind switch
        {
            BannerKind.Success => (Pal.SuccessSoft, Pal.Success, Glyph.Check),
            BannerKind.Warning => (Pal.WarningSoft, Pal.Warning, Glyph.Alert),
            BannerKind.Error => (Pal.DangerSoft, Pal.Danger, Glyph.Alert),
            _ => (Pal.AccentSoft, Pal.Accent, Glyph.Info)
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        var glyph = Glyph.Icon(icon, 22, foreground); glyph.VerticalAlignment = VerticalAlignment.Top; glyph.Margin = new Thickness(0, 2, 0, 0);
        var text = new TextBlock { Text = message, FontSize = 15, Foreground = Pal.Text, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(text, 1);
        row.Children.Add(glyph); row.Children.Add(text);
        return new Border { Background = background, BorderBrush = foreground, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12), Child = row };
    }

    /// <summary>Two or three exclusive choices side by side.</summary>
    public static (Control View, Func<int> Selected, Action<int> Select) Segmented(IReadOnlyList<string> labels, Action<int> changed, int initial = 0)
    {
        var buttons = new List<Button>();
        var grid = new Grid { ColumnSpacing = 6 };
        var selected = initial;
        void Paint()
        {
            for (var i = 0; i < buttons.Count; i++)
            {
                var on = i == selected;
                buttons[i].Background = on ? Pal.Brand : Pal.Raised;
                buttons[i].BorderBrush = on ? Pal.Brand : Pal.Border;
                ((TextBlock)buttons[i].Content!).Foreground = on ? Brushes.White : Pal.Muted;
            }
        }
        for (var i = 0; i < labels.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var index = i;
            var button = new Button
            {
                Content = new TextBlock { Text = labels[i], FontSize = 15, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap },
                MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(6, 8)
            };
            button.Click += (_, _) => { selected = index; Paint(); changed(index); };
            Grid.SetColumn(button, i); buttons.Add(button); grid.Children.Add(button);
        }
        Paint();
        return (grid, () => selected, i => { selected = i; Paint(); });
    }

    public static Control Divider() => new Border { Height = 1, Background = Pal.Subtle, Margin = new Thickness(0, 4) };

    public static Control EmptyState(string title, string message, Control? action = null)
    {
        var stack = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12, 36) };
        stack.Children.Add(new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(28), Background = Pal.AccentSoft, HorizontalAlignment = HorizontalAlignment.Center, Child = Glyph.Icon(Glyph.Info, 26, Pal.Accent) });
        ((Control)((Border)stack.Children[0]).Child!).HorizontalAlignment = HorizontalAlignment.Center;
        ((Control)((Border)stack.Children[0]).Child!).VerticalAlignment = VerticalAlignment.Center;
        stack.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Pal.Text, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = message, FontSize = 15, Foreground = Pal.Muted, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        if (action is not null) stack.Children.Add(action);
        return stack;
    }
}

public enum BannerKind { Info, Success, Warning, Error }
