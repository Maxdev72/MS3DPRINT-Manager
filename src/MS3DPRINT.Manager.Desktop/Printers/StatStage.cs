using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop.Printers;

/// <summary>
/// Big figure, title and caption on a raised surface: the look of the Home page's project counters
/// (Button.dashboard-stage), for figures that are not clickable.
/// </summary>
public sealed class StatStage : Border
{
    private readonly TextBlock _metric = new() { Text = "—", Classes = { "metric", "brand-heading" }, FontSize = Ui.Text.Display, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _caption = new() { Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis };

    public StatStage(string title, string? accentBrush = null)
    {
        CornerRadius = new(Ui.Radius.Control);
        Padding = new(24, 16);
        Bind(BackgroundProperty, this.GetResourceObservable("RaisedSurfaceBrush"));
        if (accentBrush is not null) _metric.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(accentBrush));
        _title.Text = title;
        Grid.SetColumn(_text, 1);
        _text.Children.Add(_title);
        _text.Children.Add(_caption);
        Child = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 16, Children = { _metric, _text } };
        Set("—");
    }

    private readonly StackPanel _text = new() { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

    public void Set(string metric, string? caption = null)
    {
        _metric.Text = metric;
        _caption.Text = string.IsNullOrEmpty(caption) ? " " : caption;
        AutomationProperties.SetName(this, $"{_title.Text} : {metric} {caption}".Trim());
    }
}
