using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Printers;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Provides read-only inventory data for the desktop shell.</summary>
public sealed class WorkspaceInventoryReader
{
    private static readonly string[] CollectionCategories = ["02_MODELES_3D", "03_PRODUITS_MS3DPRINT", "06_FOURNISSEURS"];

    public IReadOnlyList<CollectionItemSummary> LoadCollections(string workspaceRoot, string category)
        => new CollectionCatalog().Load(workspaceRoot, category);

    public IReadOnlyList<FilamentProfile> LoadFilaments(string workspaceRoot)
        => new FilamentStore(workspaceRoot).LoadReadable();

    public IReadOnlyList<PrinterProfile> LoadPrinters(string workspaceRoot)
        => new PrinterStore(workspaceRoot).LoadReadable();

    public IReadOnlyList<CollectionItemSummary> LoadAllCollections(string workspaceRoot)
        => CollectionCategories.SelectMany(category => LoadCollections(workspaceRoot, category))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
}
