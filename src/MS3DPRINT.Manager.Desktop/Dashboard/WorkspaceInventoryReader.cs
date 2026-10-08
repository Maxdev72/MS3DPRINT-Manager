using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Filaments;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Provides read-only inventory data for the desktop shell.</summary>
public sealed class WorkspaceInventoryReader
{
    public IReadOnlyList<CollectionItemSummary> LoadCollections(string workspaceRoot, string category)
        => new CollectionCatalog().Load(workspaceRoot, category);

    public IReadOnlyList<FilamentProfile> LoadFilaments(string workspaceRoot)
        => new FilamentStore(workspaceRoot).LoadAll();
}
