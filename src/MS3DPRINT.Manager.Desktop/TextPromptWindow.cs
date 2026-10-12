using Avalonia.Controls;
using Avalonia.Layout;

namespace MS3DPRINT.Manager.Desktop;

public sealed class TextPromptWindow : Window
{
    private readonly TextBox _value;
    public string Value => _value.Text?.Trim() ?? string.Empty;

    public TextPromptWindow(string title, string detail, string initialValue)
    {
        Title = title; Width = 460; Height = 260; MinWidth = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _value = new TextBox { Text = initialValue };
        var cancel = new Button { Content = "Annuler" };
        cancel.Click += (_, _) => Close(false);
        var save = new Button { Content = "Enregistrer" };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(Value)) Close(true); };
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14, Children = { new TextBlock { Text = detail, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } }, _value, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { cancel, save } } } };
        Opened += (_, _) => _value.Focus();
    }
}
