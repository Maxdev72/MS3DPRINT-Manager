using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Printers;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Provides read-only inventory data for the desktop shell.</summary>
public sealed class WorkspaceInventoryReader
{
    public IReadOnlyList<string> ReadErrors { get; private set; } = [];
    private static readonly string[] CollectionCategories = ["02_MODELES_3D", "03_PRODUITS_MS3DPRINT", "06_FOURNISSEURS"];

    public IReadOnlyList<CollectionItemSummary> LoadCollections(string workspaceRoot, string category)
        => new CollectionCatalog().Load(workspaceRoot, category);

    public IReadOnlyList<FilamentProfile> LoadFilaments(string workspaceRoot)
    {
        var store = new FilamentStore(workspaceRoot);
        var profiles = store.LoadReadable(); ReadErrors = store.ReadErrors;
        return profiles;
    }

    public IReadOnlyList<PrinterProfile> LoadPrinters(string workspaceRoot)
    {
        var store = new PrinterStore(workspaceRoot);
        var profiles = store.LoadReadable(); ReadErrors = store.ReadErrors;
        return profiles;
    }

    public IReadOnlyList<CollectionItemSummary> LoadAllCollections(string workspaceRoot)
        => CollectionCategories.SelectMany(category => LoadCollections(workspaceRoot, category))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
}
