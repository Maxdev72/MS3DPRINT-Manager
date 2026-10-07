using System.Windows;
namespace MS3DPRINT.Manager.App.Views;
public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string description, string value = "")
    {
        InitializeComponent(); Title = title; DescriptionText.Text = description; ValueBox.Text = value;
        MaxWidth = SystemParameters.WorkArea.Width * .9;
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }
    public string Value => ValueBox.Text.Trim();
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Value)) { ErrorText.Text = "Saisissez un nom."; return; }
        DialogResult = true;
    }
}
