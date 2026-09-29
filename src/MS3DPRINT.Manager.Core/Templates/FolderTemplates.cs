namespace MS3DPRINT.Manager.Core.Templates;

public static class FolderTemplates
{
    public static IReadOnlyList<string> Main { get; } = Array.AsReadOnly(new[]
    {
        "01_CLIENTS", "02_MODELES_3D", "03_PRODUITS_MS3DPRINT", "04_COMMUNICATION",
        "05_MACHINES_MATERIAUX", "06_FOURNISSEURS", "07_ADMINISTRATIF",
        "08_COMPTABILITE", "09_COMMERCIAL", "10_RESSOURCES_MS3DPRINT", "99_ARCHIVES"
    });

    public static IReadOnlyList<string> Client { get; } = Array.AsReadOnly(new[]
    {
        "00_CLIENT", "99_ARCHIVES"
    });

    public static IReadOnlyList<string> Project { get; } = Array.AsReadOnly(new[]
    {
        "00_BRIEF_CLIENT", "01_DEVIS_FACTURES", "02_FICHIERS_CLIENT",
        "03_CAO_3D/01_MASTER", "03_CAO_3D/02_STEP", "03_CAO_3D/03_STL",
        "04_IMPRESSION_3D/01_STL_PRODUCTION", "04_IMPRESSION_3D/02_3MF",
        "04_IMPRESSION_3D/03_PARAMETRES", "05_PRODUCTION", "06_PHOTOS_RENDUS",
        "07_LIVRAISON", "99_ARCHIVES"
    });

    public static IReadOnlyList<string> Model { get; } = Array.AsReadOnly(new[]
    {
        "01_REFERENCES", "02_SOURCE", "03_CAO_MASTER", "04_STEP", "05_STL", "06_3MF",
        "07_RENDUS", "08_PHOTOS", "99_ARCHIVES"
    });

    public static IReadOnlyList<string> Product { get; } = Array.AsReadOnly(new[]
    {
        "01_CONCEPT_REFERENCES", "02_CAO", "03_STL", "04_3MF", "05_TESTS_PROTOTYPES",
        "06_PHOTOS_RENDUS", "07_MARKETING", "08_PRIX_COUTS", "99_ARCHIVES"
    });

    public static IReadOnlyList<string> Supplier { get; } = Array.AsReadOnly(new[]
    {
        "01_CONTACT", "02_TARIFS", "03_COMMANDES", "04_FACTURES",
        "05_DOCUMENTATION", "99_ARCHIVES"
    });
}
