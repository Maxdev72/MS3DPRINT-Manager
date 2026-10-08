# Pages compactes et accessibles — Implementation Plan

> **For agentic workers:** Use subagent-driven-development to implement and review these tasks in this session.

**Goal:** rendre les documents toujours consultables, compacter les listes et agrandir/zoomer les PDF.

**Architecture:** conserver les catalogues et les services métier existants. Des ListView avec GridView partagent un composant de tableau compact et triable ; les fiches disposent d’onglets avec fichiers en premier. Les dossiers sans fiche sont consultables sans écriture implicite et offrent une complétion explicite guidée.

**Tech Stack:** WPF .NET 8 Windows, MaterialDesignInXAML, rendu PDF Windows.

**Spec:** docs/audits/2026-10-08-pages-ux-crud.md, proposition approuvée par l’utilisateur.

## Contraintes

- Préserver identifiants, protections des chemins, corbeille/restauration et brouillons.
- Conserver les quatre thèmes et l’accent. Sélection simple, ouverture double clic/Entrée ou bouton.
- Aucun profil créé implicitement à l’ouverture d’un dossier historique.
- Chaque build/test/publish possède un binlog ; builds sérialisés, tests sur fixtures.
- Remplacer uniquement MS3DPRINT Manager.exe, sauvegarde dans outputs/backups, SHA256 identique au publié, un seul exe racine.

## Lots

- [x] Tableaux : Controls/CompactTable ; ClientsView, ProjectsView, CollectionView, FilamentsView, SearchView, TrashView. Colonnes, hauteur36–40, tri, resizing, clavier et actions sur sélection. Tests comportementaux avant code, revue après GREEN.
- [x] Consultation : MainWindow navigation/management ; vues détail clients/projets/collections. Onglets Fichiers/Informations/Projets ; vue sans fiche consultable, complétion guidée client puis projet, actions métier disponibles ou expliquées. Tests ouverture sans écriture et complétion/retour.
- [x] PDF : DocumentPreviewWindow et PdfPageRenderer. Agrandir/Rétablir, zoom/largeur, défilement, page conservée, rendu adapté et borné. Tests avant code, revue après GREEN.
- [x] Navigateur partagé : FileManagement et WorkspaceFilesView. Tableau de fichiers Nom/Type/Taille/Modifié, boutons visibles utilisant sélection, ouverture doubleclic/Entrée. Tests disponibilité et absence ouverture à sélection.
- [x] Intégration : revue de chaque lot, suite complète, contrôle visuel quatre thèmes et petite fenêtre, publication, remplacement binaire et contrôle SHA256.

## Ledger

Ruling : continuer dans le dépôt courant propre sur feat/full-management-filaments — prolongement direct des corrections déjà livrées, fichiers répartis par propriétaire — les builds restent centralisés.

Ruling : conserver ListView/GridView pour la compatibilité des contrôles existants et permettre le menu contextuel — éviter une migration DataGrid qui pourrait multiplier les changements de sélection — le tri sera pris en charge par le composant partagé.
