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

    private readonly StackPanel _rows = new() { Spacing = 4 };
    private readonly TextBlock _note = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _open = new() { Content = "Ouvrir dans le slicer" };
    private readonly Button _openWith = new() { Content = "Autre application…", Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _store = new() { Content = "Ranger dans un projet…", Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
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
        var more = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _openWith, _store } };
        var folder = new Button { Content = "Afficher le dossier" };
        folder.Click += (_, _) => ShowFolder();
        var close = new Button { Content = "Fermer" };
        close.Click += (_, _) => Close(false);

        var list = new Border
        {
            Classes = { "card-raised" }, Padding = new Thickness(10, 8),
            Child = new ScrollViewer { MaxHeight = 260, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = _rows }
        };
        Layout("Téléchargement terminé", LicenceReminder, [list, more, _note], _open, close, extraAction: folder);
    }

    /// <summary>Adds files to the list (all ticked). Safe to call while the window is open.</summary>
    public void Add(IEnumerable<BufferedFile> files)
    {
        foreach (var file in files)
        {
            if (_files.Any(known => string.Equals(known.File.Path, file.Path, StringComparison.OrdinalIgnoreCase))) continue;
            var box = new CheckBox { IsChecked = true };
            box.IsCheckedChanged += (_, _) => Refresh();
            var name = new TextBlock { Text = file.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(name, file.Path);
            var size = new TextBlock { Text = SizeText(file.Length), Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8, Children = { box, name, size } };
            Grid.SetColumn(name, 1); Grid.SetColumn(size, 2);
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
        Title = _files.Count > 1 ? _files.Count + " fichiers 3D reçus" : _files.Count == 1 ? "Fichier 3D reçu" : "Téléchargement terminé";
        _open.IsEnabled = !_busy && ticked > 0;
        _open.Content = ticked > 1 ? "Ouvrir les " + ticked + " dans le slicer" : "Ouvrir dans le slicer";
        _openWith.IsVisible = ticked == 1;
        _store.IsVisible = _pickFolder is not null && ticked > 0;
        _note.Text = "Rangé dans le tampon du logiciel (pas dans vos Téléchargements).";
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
