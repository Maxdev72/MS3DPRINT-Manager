using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace MS3DPRINT.Manager.Desktop;

public sealed partial class FileBrowserView
{
    /// <summary>Copy-only drop into the displayed directory; the existing import flow owns all mutations.</summary>
    public void ConfigureFileDrop(string directory, Func<bool> available, Func<IReadOnlyList<string>, Task> import)
    {
        var heading = new TextBlock { Text = "Glissez vos fichiers ici pour les importer", FontWeight = FontWeight.SemiBold };
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        var destination = new TextBlock { Text = $"Destination : « {(folderName.Length == 0 ? directory : folderName)} » · {directory}", Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(destination, directory);
        AutomationProperties.SetName(destination, "Destination de l’import : " + directory);
        var labels = new StackPanel { Spacing = 4, Children = { heading, destination } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        grid.Children.Add(Icons.Glyph("IconUpload", 22)); Grid.SetColumn(labels, 1); grid.Children.Add(labels);
        var target = new Border { Child = grid, Classes = { "file-drop-target" }, Padding = new Thickness(14, 12), MinHeight = 64 };
        Children.Insert(0, target);
        Background = Brushes.Transparent;
        DragDrop.SetAllowDrop(this, true);
        var importing = false;
        IDataTransfer? cachedTransfer = null; string[] cachedPaths = [];
        bool Ready() => !importing && !_loading && SearchInput.IsEnabled && Rows.IsEnabled && IsEffectivelyVisible && available();
        void Reset()
        {
            target.Classes.Remove("drag-active"); target.Classes.Remove("drop-refused");
            heading.Text = "Glissez vos fichiers ici pour les importer";
            cachedTransfer = null; cachedPaths = [];
        }
        static string[] Sources(IDataTransfer data)
        {
            var items = data.TryGetFiles()?.ToArray() ?? [];
            if (items.Length == 0 || items.Any(item => item is not IStorageFile)) return [];
            var paths = items.Select(item => item.TryGetLocalPath()).ToArray();
            if (paths.Any(path => path is null || !Path.IsPathFullyQualified(path))) return [];
            return paths.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        string[] ReadSources(IDataTransfer data)
        {
            try { return Sources(data); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            { return []; }
        }
        void Over(object? sender, DragEventArgs e)
        {
            if (!ReferenceEquals(cachedTransfer, e.DataTransfer)) { cachedTransfer = e.DataTransfer; cachedPaths = ReadSources(e.DataTransfer); }
            var ready = Ready();
            var accepts = ready && cachedPaths.Length > 0 && (e.DragEffects & DragDropEffects.Copy) != 0;
            e.DragEffects = accepts ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
            target.Classes.Set("drag-active", accepts); target.Classes.Set("drop-refused", !accepts);
            heading.Text = accepts ? $"Relâchez pour importer {cachedPaths.Length} fichier(s)" :
                !ready ? "Import indisponible pendant le traitement ou la confirmation" : "Déposez uniquement des fichiers locaux à copier";
        }
        AddHandler(DragDrop.DragEnterEvent, Over); AddHandler(DragDrop.DragOverEvent, Over);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => Reset());
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            e.Handled = true;
            var allowedCopy = (e.DragEffects & DragDropEffects.Copy) != 0;
            var paths = ReadSources(e.DataTransfer);
            var accepts = Ready() && allowedCopy && paths.Length > 0;
            e.DragEffects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
            Reset();
            if (!accepts) return;
            importing = true;
            try { await import(paths); }
            finally { importing = false; Reset(); }
        });
        DetachedFromVisualTree += (_, _) => Reset();
    }
}
