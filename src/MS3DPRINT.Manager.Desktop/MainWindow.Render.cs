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
            studio.SaveRequested += SaveViewerCapture;
            RenderWorkspacePanel.Child = studio;
        }
        await studio.ActivateAsync();
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

    /// <summary>Silk filaments shine like metal, PETG is glossy, everything else renders matte.</summary>
    public static string RenderFinish(string materialAndName)
        => materialAndName.Contains("phospho", StringComparison.OrdinalIgnoreCase) || materialAndName.Contains("glow", StringComparison.OrdinalIgnoreCase) ? "glow"
            : materialAndName.Contains("silk", StringComparison.OrdinalIgnoreCase) || materialAndName.Contains("soie", StringComparison.OrdinalIgnoreCase) ? "silk"
            : materialAndName.Contains("PETG", StringComparison.OrdinalIgnoreCase) ? "glossy" : "matte";
}
