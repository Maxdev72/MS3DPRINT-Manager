using Avalonia.Controls;
using Avalonia.Layout;

namespace MS3DPRINT.Manager.Desktop;

public sealed class FolderBrowserWindow : Window
{
    private readonly string _root;
    private readonly Func<string, Task> _openFile;
    private readonly StackPanel _entries = new() { Spacing = 8 };

    public FolderBrowserWindow(string root, Func<string, Task> openFile)
    {
        _root = Path.GetFullPath(root); _openFile = openFile;
        Title = Path.GetFileName(_root); Width = 900; Height = 680; MinWidth = 600;
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14,
            Children = { new TextBlock { Text = "Contenu du dossier", FontSize = 21, FontWeight = Avalonia.Media.FontWeight.SemiBold }, new ScrollViewer { Content = _entries } } };
        Opened += (_, _) => Load(_root);
    }

    private void Load(string directory)
    {
        _entries.Children.Clear();
        var back = new Button { Content = "← Retour", IsVisible = !string.Equals(directory, _root, StringComparison.OrdinalIgnoreCase), HorizontalAlignment = HorizontalAlignment.Left };
        back.Click += (_, _) => Load(Path.GetDirectoryName(directory)!); _entries.Children.Add(back);
        foreach (var path in Directory.EnumerateFileSystemEntries(directory).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            var folder = Directory.Exists(path);
            var item = new Button { Content = (folder ? "▸  " : "•  ") + Path.GetFileName(path), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Classes = { "workspace-card" } };
            if (folder) item.Click += (_, _) => Load(path); else item.Click += async (_, _) => await _openFile(path);
            _entries.Children.Add(item);
        }
    }
}
