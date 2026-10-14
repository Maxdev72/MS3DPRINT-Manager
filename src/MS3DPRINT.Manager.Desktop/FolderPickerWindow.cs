using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>
/// Chooses a destination folder inside the workspace, in the application: the system folder window would open Explorer and let the
/// user wander out of the workspace. Folders open with a click, "Dossier parent" goes back up (never above the workspace).
/// </summary>
public sealed class FolderPickerWindow : DialogWindow
{
    private readonly string _root;
    private readonly string? _excluded;
    private readonly TextBlock _path = new() { FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly Button _up = new() { Content = Icons.Label("IconArrowLeft", "Dossier parent"), HorizontalAlignment = HorizontalAlignment.Left };
    private string _current;

    /// <summary>The folder chosen with the confirm button.</summary>
    public string? SelectedFolder { get; private set; }

    /// <param name="root">The workspace: the picker never goes above it.</param>
    /// <param name="start">The folder shown first (the item's current folder is a good choice).</param>
    /// <param name="excluded">A folder that cannot be a destination, nor anything inside it (the folder being moved).</param>
    public FolderPickerWindow(string root, string start, string heading, string? description, string? excluded = null, string confirmLabel = "Déplacer ici")
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _excluded = excluded is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(excluded));
        _current = Inside(start) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(start)) : _root;
        Title = heading;
        Width = 600; Height = 660; MinWidth = 480; MinHeight = 480;

        var confirm = new Button { Content = confirmLabel };
        confirm.Click += (_, _) => { SelectedFolder = _current; Close(true); };
        _up.Click += (_, _) => { if (Path.GetDirectoryName(_current) is { } parent && Inside(parent)) Load(parent); };

        var frame = new Border
        {
            Classes = { "card-raised" }, Padding = new Thickness(6),
            Child = new ScrollViewer { Height = 380, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = _list }
        };
        var body = new StackPanel
        {
            Spacing = 8,
            Children = { FormKit.SectionLabel("Dossier choisi"), _path, _up, frame }
        };
        Layout(heading, description, [body], confirm);
        Opened += (_, _) => Load(_current);
    }

    private bool Inside(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(full, _root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsExcluded(string path)
        => _excluded is not null && (string.Equals(path, _excluded, StringComparison.OrdinalIgnoreCase)
                                     || path.StartsWith(_excluded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    private void Load(string directory)
    {
        _current = directory;
        var relative = Path.GetRelativePath(_root, directory);
        _path.Text = relative == "." ? Path.GetFileName(_root) : Path.GetFileName(_root) + " › " + string.Join(" › ", relative.Split(Path.DirectorySeparatorChar));
        _up.IsEnabled = !string.Equals(directory, _root, StringComparison.OrdinalIgnoreCase);
        _list.Children.Clear();

        IEnumerable<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(directory)
                .Where(path => !Path.GetFileName(path).StartsWith('.') && !IsExcluded(path) && IsPlainFolder(path))
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _list.Children.Add(new TextBlock { Text = "Ce dossier ne peut pas être lu : " + exception.Message, Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(10) });
            return;
        }

        foreach (var folder in folders)
        {
            var entry = new Button
            {
                Content = Icons.Label("IconProjects", Path.GetFileName(folder)), Classes = { "ghost" },
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left
            };
            var target = folder;
            entry.Click += (_, _) => Load(target);
            _list.Children.Add(entry);
        }
        if (_list.Children.Count == 0)
            _list.Children.Add(new TextBlock { Text = "Aucun sous-dossier ici : « Déplacer ici » choisit ce dossier.", Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(10) });
    }

    /// <summary>Links and junctions are not offered: they could lead out of the workspace (cloud placeholders are fine).</summary>
    private static bool IsPlainFolder(string path)
    {
        try { WorkspacePathSafety.EnsureNoLinks(path); return true; }
        catch (IOException) { return false; }
    }
}
