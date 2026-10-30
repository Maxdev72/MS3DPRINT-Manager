using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Naming;
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
        var folderName = NameNormalizer.Normalize(name);
        if (folderName.Length == 0)
            throw new ArgumentException("Le nom doit contenir au moins une lettre ou un chiffre.", nameof(name));
        var root = Path.GetFullPath(workspaceRoot);
        var created = new FolderTreeService().CreateTree(Path.Combine(root, category, folderName), template);
        return new CollectionItemSummary(name.Trim(), created.DestinationPath, DateTimeOffset.UtcNow);
    }
}
