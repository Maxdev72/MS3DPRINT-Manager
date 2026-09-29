using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.Core.Naming;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.App.Views;

public partial class CreateNamedItemWindow : Window, ICreatedFolderDialog
{
    private readonly string _targetRoot;
    private readonly IReadOnlyList<string> _template;
    private readonly FolderTreeService _folders;

    public CreateNamedItemWindow(string title, string targetRoot, IReadOnlyList<string> template, FolderTreeService folders)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        _targetRoot = targetRoot;
        _template = template;
        _folders = folders;
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        MaxWidth = SystemParameters.WorkArea.Width * 0.9;
        Loaded += (_, _) => NameBox.Focus();
    }

    public string? CreatedPath { get; private set; }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (PreviewBox is not null) PreviewBox.Text = NameNormalizer.Normalize(NameBox.Text);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (PreviewBox.Text.Length == 0)
        {
            ErrorText.Text = "Saisissez un nom contenant au moins une lettre ou un chiffre.";
            return;
        }

        try
        {
            CreatedPath = _folders.CreateTree(Path.Combine(_targetRoot, PreviewBox.Text), _template).DestinationPath;
            if (MessageBox.Show(this, "Dossier créé. L’ouvrir dans l’Explorateur ?", "MS3DPRINT", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                try { ExplorerService.Open(CreatedPath); }
                catch (Exception exception) { MessageBox.Show(this, UiErrorMessages.For(exception), "MS3DPRINT — Explorateur", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
            DialogResult = true;
        }
        catch (Exception exception) { ErrorText.Text = UiErrorMessages.For(exception); }
    }
}
