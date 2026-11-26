using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Desktop.Dashboard;
using MS3DPRINT.Manager.Desktop.Rendering;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Product render page (Blender): created on first visit, its images filed like the 3D viewer captures.</summary>
public partial class MainWindow
{
    private async Task LoadRenderPageAsync()
    {
        if (RenderWorkspacePanel.Child is not RenderStudioView studio)
        {
            // Kept beside the window placement file, so tests that redirect that file never touch this PC's preferences.
            var placementFile = Environment.GetEnvironmentVariable("MS3DPRINT_WINDOW_FILE");
            studio = new RenderStudioView(LoadRenderColors, new RenderPreferencesStore(placementFile is null ? null : Path.Combine(Path.GetDirectoryName(placementFile)!, "render.json")));
            studio.SaveFiles = SaveRendersAsync;
            RenderWorkspacePanel.Child = studio;
        }
        await studio.ActivateAsync();
    }

    /// <summary>
    /// Files the rendered images in the photos and renders folder of the model's project (one notification with « Ouvrir le
    /// dossier »); for a model outside any project, asks for a folder. The temporary copies are deleted afterwards.
    /// </summary>
    private async Task<string?> SaveRendersAsync(IReadOnlyList<string> temporaryFiles, string modelPath, bool chooseFolder)
    {
        modelPath = RenderModelKey.File(modelPath); // an object of a 3MF taken apart is filed with its file
        try
        {
            var root = WorkspaceRoot;
            var saved = new List<string>();
            if (!chooseFolder && MS3DPRINT.Manager.Core.Projects.ProjectLocator.PhotosFolder(modelPath) is { } photos && Path.GetFullPath(photos).StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            {
                saved.AddRange(await Task.Run(() =>
                {
                    var files = new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(root);
                    if (!Directory.Exists(photos)) files.CreateFolder(Path.GetDirectoryName(photos)!, Path.GetFileName(photos));
                    return temporaryFiles.Select(file => files.Import(file, photos)).ToList();
                }));
            }
            else
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = "Où enregistrer les images du rendu ?" });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } target) return null;
                saved.AddRange(await Task.Run(() => temporaryFiles.Select(file =>
                {
                    var destination = Path.Combine(target, Path.GetFileName(file));
                    File.Copy(file, destination, overwrite: true);
                    return destination;
                }).ToList()));
            }
            if (saved.Count == 0) return null;
            Notify(saved.Count == 1 ? "Image enregistrée : " + Path.GetFileName(saved[0]) : $"{saved.Count} fichiers enregistrés dans {Path.GetFileName(Path.GetDirectoryName(saved[0]))}",
                action: ("Ouvrir le dossier", () => RevealInExplorer(saved[0])));
            return Path.GetDirectoryName(saved[0]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); return null; }
        finally
        {
            foreach (var file in temporaryFiles) { try { File.Delete(file); } catch (IOException) { } }
        }
    }

    /// <summary>Opens the file's folder in the Explorer with the file selected.</summary>
    private static void RevealInExplorer(string path)
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open") { UseShellExecute = false };
            // Explorer only reads /select,"path": quoting the whole argument (ArgumentList) opens Documents instead.
            if (OperatingSystem.IsWindows()) start.Arguments = $"/select,\"{Path.GetFullPath(path)}\"";
            else start.ArgumentList.Add(Path.GetDirectoryName(Path.GetFullPath(path))!);
            System.Diagnostics.Process.Start(start)?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) { ErrorLog.Write(exception); }
    }

    /// <summary>Leaves the 3D viewer for the render page with the same model.</summary>
    private async void RenderFromViewer(string modelPath)
    {
        CloseModelViewer();
        await NavigateToWorkspaceAsync("Rendu produit");
        if (RenderWorkspacePanel.Child is RenderStudioView studio) await studio.LoadModelAsync(modelPath);
    }

    /// <summary>The colours of the workshop filaments (their finish guessed from the name); the page adds the bundled palettes.</summary>
    private IReadOnlyList<RenderColor> LoadRenderColors()
    {
        var stock = new WorkspaceInventoryReader().LoadFilaments(WorkspaceRoot)
            .Select(f => (Filament: f, Hex: FilamentHex(f.Color)))
            .Where(x => PreviewColorParser.TryParse(x.Hex, out _))
            .Select(x => new RenderColor($"{x.Filament.Brand} {x.Filament.Name} ({x.Filament.Material})", x.Hex!.ToUpperInvariant(), RenderFinish($"{x.Filament.Material} {x.Filament.Name}")));
        return stock.DistinctBy(c => c.Key).ToList();
    }

    /// <summary>Carbon or glass filled (CF, GF) renders grainy, silk shines like metal, PETG is glossy, everything else matte.</summary>
    public static string RenderFinish(string materialAndName)
        => System.Text.RegularExpressions.Regex.IsMatch(materialAndName, @"\bCF\b|carbon", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "carbon"
            : System.Text.RegularExpressions.Regex.IsMatch(materialAndName, @"\bGF\b|glass|verre", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "glassfiber"
            : materialAndName.Contains("phospho", StringComparison.OrdinalIgnoreCase) || materialAndName.Contains("glow", StringComparison.OrdinalIgnoreCase) ? "glow"
            : materialAndName.Contains("silk", StringComparison.OrdinalIgnoreCase) || materialAndName.Contains("soie", StringComparison.OrdinalIgnoreCase) ? "silk"
            : materialAndName.Contains("PETG", StringComparison.OrdinalIgnoreCase) ? "glossy" : "matte";
}
