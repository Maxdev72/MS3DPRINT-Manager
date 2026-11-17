using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Templates;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Documents page: the company folders of the workspace (administrative, accounting, commercial…), opened in the file browser.</summary>
public partial class MainWindow
{
    private const string DocumentRowColumns = "2*,120,110";

    // Folder name in FolderTemplates.Main, label, what to put in it.
    private static readonly (string Folder, string Label, string Hint, string[] Sub)[] DocumentFolders =
    [
        ("07_ADMINISTRATIF", "Administratif", "Statuts, immatriculation, assurances, banque, courriers", ["01_STATUTS_IMMATRICULATION", "02_ASSURANCES", "03_BANQUE", "04_IMPOTS_URSSAF", "05_CONTRATS_ABONNEMENTS", "06_COURRIERS"]),
        ("08_COMPTABILITE", "Comptabilité", "Factures, déclarations, impôts, cotisations", ["01_FACTURES_VENTES", "02_FACTURES_ACHATS", "03_DECLARATIONS_TVA_CA", "04_RELEVES_BANCAIRES", "05_BILANS_JUSTIFICATIFS"]),
        ("09_COMMERCIAL", "Commercial", "Prospection, tarifs, contrats, conditions de vente", ["01_PROSPECTION", "02_DEVIS_TARIFS", "03_CONTRATS_CGV", "04_SALONS_EVENEMENTS"]),
        ("04_COMMUNICATION", "Communication", "Logo, réseaux sociaux, supports de présentation", ["01_LOGO_CHARTE", "02_RESEAUX_SOCIAUX", "03_SITE_WEB", "04_SUPPORTS_IMPRIMES"]),
        ("10_RESSOURCES_MS3DPRINT", "Ressources", "Modèles de documents, tutoriels, références", ["01_MODELES_DOCUMENTS", "02_TUTORIELS", "03_REFERENCES"]),
        ("99_ARCHIVES", "Archives", "Documents clos à garder", [])
    ];

    private async Task LoadDocumentsAsync()
    {
        var root = WorkspaceRoot;
        var load = StartWorkspaceLoad("documents", root);
        DocumentsList.Children.Clear();
        try
        {
            var rows = await Task.Run(() => DocumentFolders.Where(item => FolderTemplates.Main.Contains(item.Folder)).Select(item =>
            {
                var path = Path.Combine(root, item.Folder);
                // Starter subfolders only in an empty folder, so ones the user deletes do not come back.
                var fresh = !Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any();
                Directory.CreateDirectory(path);
                if (fresh) foreach (var sub in item.Sub) Directory.CreateDirectory(Path.Combine(path, sub));
                return (item.Label, item.Hint, Path: path, Count: Directory.EnumerateFileSystemEntries(path).Count(), Date: Directory.GetLastWriteTime(path));
            }).ToArray());
            if (!IsWorkspaceLoadCurrent(load)) return;
            DocumentsList.Children.Add(CreateCompactTableHeader(DocumentRowColumns, "DOSSIER", "CONTENU", "MODIFIÉ LE"));
            foreach (var row in rows)
            {
                var open = new Button { Tag = row.Path, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                open.Click += OpenFolder_Click;
                var title = new StackPanel { Spacing = 1 };
                title.Children.Add(CreateCompactTableCell(row.Label, prominent: true));
                title.Children.Add(new TextBlock { Text = row.Hint, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });
                DocumentsList.Children.Add(CreateActionRow(DocumentRowColumns, open, null, title,
                    CreateCompactTableCell(row.Count == 0 ? "Vide" : row.Count == 1 ? "1 élément" : $"{row.Count} éléments"),
                    CreateCompactTableCell($"{row.Date:dd/MM/yyyy}")));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!IsWorkspaceLoadCurrent(load)) return;
            DocumentsList.Children.Clear();
            DocumentsList.Children.Add(CreateReadErrorState("Documents indisponibles", "Vérifiez l’accès au dossier de l’atelier, puis réessayez.", exception, LoadDocumentsAsync));
        }
    }
}
