using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.ModelSearch;

namespace MS3DPRINT.Manager.Desktop.ModelSearch;

/// <summary>
/// The window shown, centred on the application, when the browser has finished downloading 3D files (or a ZIP that held some).
/// There is only ever one: files that arrive while it is open are added to its list. "Ouvrir dans le slicer" sends everything
/// ticked to a single slicer process, and the button stays disabled while it starts so a second click cannot start another one.
/// </summary>
public sealed class DownloadDialog : DialogWindow
{
    private const string LicenceReminder = "Vérifiez la licence de chaque modèle avant de l’imprimer pour un client : beaucoup sont réservés à l’usage personnel (CC BY-NC, « non commercial »).";

    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly TextBlock _summary = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _note = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _open = new() { Content = "Ouvrir dans le slicer" };
    private readonly Button _openWith = new() { Content = Icons.Label("IconOpen", "Autre application", 15), Classes = { "ghost" } };
    private readonly Button _store = new() { Content = Icons.Label("IconMove", "Ranger dans un projet", 15), Classes = { "ghost" } };
    private readonly List<(BufferedFile File, CheckBox Box, Control Row)> _files = [];
    private readonly Func<Task<string?>>? _pickFolder;
    private bool _busy;

    /// <param name="pickFolder">Asks the user for a folder of the workspace (null: the "store" action is not offered).</param>
    public DownloadDialog(Func<Task<string?>>? pickFolder = null)
    {
        _pickFolder = pickFolder;
        Title = "Téléchargement terminé";
        Width = 560; MinWidth = 440; SizeToContent = SizeToContent.Height; MaxHeight = 640;
        CanResize = false;

        _open.Click += async (_, _) => await OpenAsync();
        _openWith.Click += (_, _) => OpenWith();
        _store.Click += async (_, _) => await StoreAsync();
        var folder = new Button { Content = Icons.Label("IconProjects", "Afficher le dossier", 15), Classes = { "ghost" } };
        folder.Click += (_, _) => ShowFolder();
        // the three side actions are quiet links under the list; the footer keeps only "Fermer" and the main action
        var more = new WrapPanel { Orientation = Orientation.Horizontal, Children = { _store, _openWith, folder } };
        var close = new Button { Content = "Fermer" };
        close.Click += (_, _) => Close(false);

        var list = new Border
        {
            Classes = { "card-raised" }, Padding = new Thickness(6),
            Child = new ScrollViewer { MaxHeight = 260, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = _rows }
        };
        // the licence reminder is a calm note of its own, not a paragraph under the heading
        var licence = new Border
        {
            Background = (IBrush?)Application.Current?.FindResource("WarningSoftBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10,
                Children = { Icons.Glyph("IconInfo", 16), new TextBlock { Text = LicenceReminder, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center } }
            }
        };
        Grid.SetColumn(((Grid)licence.Child).Children[1], 1);
        Layout("Téléchargement terminé", null, [_summary, list, licence, more, _note], _open, close);
    }

    /// <summary>Adds files to the list (all ticked). Safe to call while the window is open.</summary>
    public void Add(IEnumerable<BufferedFile> files)
    {
        foreach (var file in files)
        {
            if (_files.Any(known => string.Equals(known.File.Path, file.Path, StringComparison.OrdinalIgnoreCase))) continue;
            var box = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
            box.IsCheckedChanged += (_, _) => Refresh();
            var name = new TextBlock { Text = Path.GetFileNameWithoutExtension(file.Name), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.Medium };
            ToolTip.SetTip(name, file.Path);
            var format = new Border { Classes = { "badge", "info" }, Child = new TextBlock { Text = Path.GetExtension(file.Name).TrimStart('.').ToUpperInvariant() } };
            var size = new TextBlock { Text = SizeText(file.Length), Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, MinWidth = 52, TextAlignment = TextAlignment.Right };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), ColumnSpacing = 10, Children = { box, Icons.Glyph("IconCube", 18), name, format, size } };
            Grid.SetColumn(grid.Children[1], 1); Grid.SetColumn(name, 2); Grid.SetColumn(format, 3); Grid.SetColumn(size, 4);
            var row = new Border { Padding = new Thickness(8, 7), CornerRadius = new CornerRadius(8), Child = grid };
            _files.Add((file, box, row));
            _rows.Children.Add(row);
        }
        Refresh();
    }

    /// <summary>The files listed, so that the buffer clean-up never removes what the user is looking at.</summary>
    public IReadOnlyList<string> Paths => _files.Select(item => item.File.Path).ToList();

    /// <summary>Shows a ZIP that could not give any file: the message replaces nothing already listed.</summary>
    public void Say(string message) => _note.Text = message;

    private void Refresh()
    {
        var ticked = _files.Count(item => item.Box.IsChecked == true);
        var heading = _files.Count > 1 ? _files.Count + " modèles 3D reçus" : _files.Count == 1 ? "Modèle 3D reçu" : "Téléchargement terminé";
        Title = heading;
        if (HeadingText is { } headingText) headingText.Text = heading;
        // a tick box only makes sense when there is a choice to make
        foreach (var item in _files) item.Box.IsVisible = _files.Count > 1;
        var weight = _files.Sum(item => item.File.Length);
        _summary.Text = _files.Count == 0 ? string.Empty
            : (_files.Count > 1 ? _files.Count + " fichiers · " : "") + SizeText(weight) + " · dans le tampon du logiciel";
        _open.IsEnabled = !_busy && ticked > 0;
        _open.Content = ticked > 1 ? "Ouvrir les " + ticked + " dans le slicer" : "Ouvrir dans le slicer";
        _openWith.IsVisible = ticked == 1;
        _store.IsVisible = _pickFolder is not null && ticked > 0;
        _note.Text = string.Empty;
    }

    /// <summary>Moves the ticked files out of the buffer into a folder of the workspace: what is kept there is no longer cleaned up.</summary>
    private async Task StoreAsync()
    {
        if (_pickFolder is null || _busy) return;
        var paths = Ticked();
        if (paths.Count == 0) return;
        _busy = true;
        try
        {
            var folder = await _pickFolder();
            if (string.IsNullOrWhiteSpace(folder)) return;
            IReadOnlyList<string> moved;
            try { moved = DownloadBuffer.MoveModels(paths, folder); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _note.Text = "Impossible de ranger ces fichiers : " + exception.Message;
                return;
            }
            // the rows of the files that left the buffer go; the others stay in the list
            foreach (var item in _files.Where(item => !File.Exists(item.File.Path)).ToList())
            {
                _rows.Children.Remove(item.Row);
                _files.Remove(item);
            }
            Refresh();
            _note.Text = moved.Count + (moved.Count > 1 ? " fichiers rangés dans « " : " fichier rangé dans « ") + Path.GetFileName(folder.TrimEnd('\\', '/')) + " ».";
        }
        finally { _busy = false; }
    }

    private List<string> Ticked()
        => _files.Where(item => item.Box.IsChecked == true).Select(item => item.File.Path)
            .Where(path => File.Exists(path) && DownloadBuffer.IsModelFile(path) && !ExecutableFileGuard.IsExecutable(path)).ToList();

    private async Task OpenAsync()
    {
        if (_busy) return;
        _busy = true;
        _open.IsEnabled = false;
        try
        {
            var paths = Ticked();
            if (paths.Count == 0) { _note.Text = "Ces fichiers ne sont plus dans le tampon."; return; }
            if (paths.Count == 1) { await OpenOneAsync(paths[0]); return; }
            // several files: ONE slicer process with all of them, never one process per file
            var slicer = SlicerLauncher.TryOpen(paths);
            _note.Text = slicer is null
                ? "Aucun slicer (Bambu Studio, OrcaSlicer) n’a été trouvé : décochez pour n’ouvrir qu’un fichier, ou ouvrez le dossier."
                : paths.Count + " fichiers ouverts ensemble dans " + slicer + ".";
        }
        finally
        {
            // the slicer takes a moment to start: a quick second click must not launch it again
            await Task.Delay(TimeSpan.FromSeconds(3));
            _busy = false;
            _open.IsEnabled = Ticked().Count > 0;
        }
    }

    /// <summary>One file: the application Windows has for its type, else the slicer installed here, else the "Open with" window.</summary>
    private async Task OpenOneAsync(string path)
    {
        try
        {
            if (Launcher is { } launcher && await launcher.LaunchUriAsync(new Uri(path))) return;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UriFormatException or System.ComponentModel.Win32Exception)
        {
            // no application for this type
        }
        if (SlicerLauncher.TryOpen(path) is { } slicer) { _note.Text = "Ouvert dans " + slicer + "."; return; }
        OpenWithCore(path);
    }

    private void OpenWith()
    {
        if (Ticked() is [var path]) OpenWithCore(path);
    }

    private void OpenWithCore(string path)
    {
        try { DesktopPreviewLauncher.OpenWith?.Invoke(path); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _note.Text = "Impossible d’ouvrir ce fichier avec une autre application : " + exception.Message;
        }
    }

    private void ShowFolder()
    {
        var first = _files.Select(item => item.File.Path).FirstOrDefault(File.Exists);
        if (first is null) { _note.Text = "Ces fichiers ne sont plus dans le tampon."; return; }
        try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + first + "\"") { UseShellExecute = false }); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _note.Text = "Impossible d’ouvrir le dossier : " + exception.Message;
        }
    }

    internal static string SizeText(long bytes)
    {
        var french = CultureInfo.GetCultureInfo("fr-FR");
        return bytes >= 1024 * 1024 ? (bytes / 1048576d).ToString("0.#", french) + " Mo" : Math.Max(1, bytes / 1024d).ToString("0", french) + " Ko";
    }
}
