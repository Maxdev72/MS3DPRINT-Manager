using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop;

public sealed class TextPromptWindow : DialogWindow
{
    private readonly TextBox _value;
    private readonly TextBlock _error = FormKit.ErrorText();
    public string Value => _value.Text?.Trim() ?? string.Empty;

    public TextPromptWindow(string title, string detail, string initialValue)
    {
        Title = title; Width = 480; Height = 300; MinWidth = 400;
        _value = new TextBox { Text = initialValue };
        var save = new Button { Content = "Enregistrer" };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(Value)) { FormKit.ShowError(_error, _value, "Le nom ne peut pas être vide."); return; }
            Close(true);
        };
        Layout(title, detail, [FormKit.Field("Nom", _value), _error], save);
        Opened += (_, _) => { _value.Focus(); _value.SelectAll(); };
    }
}
