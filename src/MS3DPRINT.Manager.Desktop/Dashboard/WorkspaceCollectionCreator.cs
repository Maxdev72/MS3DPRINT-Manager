using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Templates;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

public sealed class WorkspaceCollectionCreator
{
    public CollectionItemSummary Create(string workspaceRoot, string category, string name)
    {
        var template = category switch
        {
            "02_MODELES_3D" => FolderTemplates.Model,
            "03_PRODUITS_MS3DPRINT" => FolderTemplates.Product,
            "06_FOURNISSEURS" => FolderTemplates.Supplier,
            _ => throw new ArgumentException("La catégorie est invalide.", nameof(category))
        };
        var root = Path.GetFullPath(workspaceRoot);
        var created = new FolderTreeService().CreateTree(Path.Combine(root, category, name.Trim()), template);
        return new CollectionItemSummary(Path.GetFileName(created.DestinationPath), created.DestinationPath, DateTimeOffset.UtcNow);
    }
}
