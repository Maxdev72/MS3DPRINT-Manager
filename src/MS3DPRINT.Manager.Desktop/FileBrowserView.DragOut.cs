using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace MS3DPRINT.Manager.Desktop;

public sealed partial class FileBrowserView
{
    /// <summary>
    /// Exports real local files to Explorer and other native applications.
    /// A normal click continues to open the file; dragging always requests Copy.
    /// </summary>
    private void AttachOutgoingFileDrag(Control handle, string path)
    {
        PointerPressedEventArgs? press = null;
        Point origin = default;
        var dragging = false;

        handle.PointerPressed += (_, e) =>
        {
            press = null;
            if (dragging || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed
                || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0
                || !Rows.IsEnabled || !(FileCommandsAvailable?.Invoke() ?? true)) return;
            press = e;
            origin = e.GetPosition(handle);
        };

        handle.PointerReleased += (_, _) => press = null;
        handle.PointerCaptureLost += (_, _) => press = null;
        handle.DetachedFromVisualTree += (_, _) => press = null;

        handle.PointerMoved += async (_, e) =>
        {
            if (press is null || dragging) return;
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            {
                press = null;
                return;
            }
            var position = e.GetPosition(handle);
            if (Math.Abs(position.X - origin.X) < 6 && Math.Abs(position.Y - origin.Y) < 6) return;

            var pressed = press;
            press = null;
            if (!Rows.IsEnabled || !(FileCommandsAvailable?.Invoke() ?? true)) return;

            // Selected files remain grouped even when they span several pages.
            // A non-selected row starts a drag of that file alone.
            var paths = _selectedFiles.Contains(path) ? _selectedFiles.ToArray() : [path];
            var known = _entries.Where(entry => !entry.IsDirectory)
                .Select(entry => entry.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (paths.Length == 0 || paths.Any(p => !known.Contains(p) || !File.Exists(p))) return;

            dragging = true;
            try
            {
                var provider = TopLevel.GetTopLevel(handle)?.StorageProvider;
                if (provider is null) return;
                var transfer = new DataTransfer();
                foreach (var filePath in paths)
                {
                    var file = await provider.TryGetFileFromPathAsync(filePath);
                    if (file is null) return;
                    transfer.Add(DataTransferItem.CreateFile(file));
                }
                // Do not dispose transfer here: Avalonia owns it after drag starts.
                await DragDrop.DoDragDropAsync(pressed, transfer, DragDropEffects.Copy);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                // The source files are never moved or deleted on a failed export.
                System.Diagnostics.Debug.WriteLine("Glisser-déposer sortant indisponible : " + exception.Message);
            }
            finally { dragging = false; }
        };
    }
}
