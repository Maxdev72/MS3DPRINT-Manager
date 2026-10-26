using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Recognition;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>What the entry windows share to read an invoice, a receipt or a screenshot: choosing a file, pasting a capture, dropping a file, and explaining what was read.</summary>
public static class DocumentReadingUi
{
    public static readonly string[] Extensions = [".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"];
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>What the reader knows about the user (company, suppliers, clients). The main window refreshes it before it opens an entry window.</summary>
    public static AnalysisContext Context { get; set; } = new();

    public static FilePickerFileType FileType { get; } = new("Facture, ticket ou capture (PDF ou image)") { Patterns = ["*.pdf", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.tif", "*.tiff", "*.webp"] };

    public static bool IsReadable(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static async Task<string?> PickAsync(TopLevel owner, string title)
    {
        if (owner.StorageProvider is not { } storage) return null;
        var chosen = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false, FileTypeFilter = [FileType] });
        return chosen.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>The picture (or file) on the clipboard, saved as a PNG in a temporary folder so that it can be read and then kept as the supporting document. Null when the clipboard holds neither.</summary>
    public static async Task<string?> PasteAsync(TopLevel owner)
    {
        if (owner.Clipboard is not { } clipboard) return null;
        try
        {
            if (await clipboard.TryGetFilesAsync() is { Length: > 0 } files && files[0].TryGetLocalPath() is { } copied && IsReadable(copied)) return copied;
            if (await clipboard.TryGetBitmapAsync() is not { } bitmap) return null;
            var folder = Path.Combine(Path.GetTempPath(), "MS3DPRINT-captures");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            bitmap.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException) { return null; }
    }

    /// <summary>Lets a file be dropped on the window; <paramref name="load"/> receives its path.</summary>
    public static void EnableDrop(Control target, Action<string> load)
    {
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.TryGetFile() is { } f && f.TryGetLocalPath() is { } p && IsReadable(p) ? DragDropEffects.Copy : DragDropEffects.None);
        target.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (e.DataTransfer.TryGetFile() is { } file && file.TryGetLocalPath() is { } path && IsReadable(path)) { e.Handled = true; load(path); }
        });
    }

    /// <summary>Several sentences for the note above the form: what was read and from where, what to check, and what the document seems to be.</summary>
    public static string Describe(DocumentReading reading, DocumentDirection expected)
        => ReadingSummary.Describe(reading, expected).Replace("dans les achats", "dans la page Achats").Replace("dans les recettes", "dans la page Recettes");

    public static string Money(decimal value) => value.ToString("0.00", French);
}
