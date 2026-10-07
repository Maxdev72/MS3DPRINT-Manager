# Full Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Fournir une gestion complète de chaque module et une bibliothèque de filaments.
**Architecture:** Moteur de fichiers et corbeille communs, fiches JSON identifiées et chemins relatifs, vues WPF réutilisant les mêmes actions. Compatibilité avec les dossiers et profils existants.
**Tech Stack:** .NET 8, WPF, MaterialDesignThemes 5.3.2, System.Text.Json, xUnit.
**Spec:** `docs/superpowers/specs/2026-10-07-full-management.md`.

## Global Constraints

- Codes clients, identifiants et références projets restent stables.
- Aucune opération de test sur les données réelles.
- Un seul `MS3DPRINT Manager.exe` dans le dossier livré.
- Pas d’écrasement d’un fichier ou dossier existant par une opération utilisateur.
- Journal de corbeille et métadonnées dans `.ms3dprint-manager`.
- Tous les builds/tests/publish ont un binlog sous `outputs/logs/`.

### Task 1: Moteur commun de fichiers et corbeille
**Files:** Créer `Core/Workspace/ManagedFileService.cs`, `TrashEntry.cs`, tests `Workspace/ManagedFileServiceTests.cs`.
**Produces:** `ManagedFileService(string root)`, `string Rename(string path,string newName)`, `string Move(string path,string destinationDirectory)`, `string CreateFolder(string parent,string name)`, `string Import(string sourceFile,string destinationDirectory)`, `TrashEntry Trash(string path,IReadOnlyList<string>? companionPaths=null,string? label=null)`, `IReadOnlyList<TrashEntry> ListTrash()`, `void Restore(Guid id)`.
`TrashEntry` expose `Id`, `Label`, `DeletedAt`.
- [x] Écrire et observer les tests de renommage, déplacement, collisions, sortie de l’espace, liens, corbeille avec plusieurs fichiers et restauration conflictuelle.
```csharp
var entry = service.Trash(folder, [profilePath], "Client");
Assert.False(Directory.Exists(folder));
service.Restore(entry.Id);
Assert.Equal("document", File.ReadAllText(Path.Combine(folder, "test.txt")));
Assert.True(File.Exists(profilePath));
```
- [x] Implémenter validations, mouvements sans écrasement, manifeste et rollback.
- [x] Vérifier tests ciblés ; revue du moteur avant intégration.

### Task 2: Bibliothèque de filaments
**Files:** Créer `Core/Filaments/FilamentProfile.cs`, `FilamentStore.cs`, `App/Views/FilamentsView.xaml(.cs)`, `FilamentEditorWindow.xaml(.cs)`, tests dédiés.
**Consumes:** `ManagedFileService.Trash` et `Restore` pour les fiches JSON.
**Produces:** `FilamentStore(string workspaceRoot)`, méthodes `LoadAll`, `Create`, `Update`, `Duplicate`, `Trash`. `FilamentsView(string workspaceRoot)` et `Task RefreshAsync()`.
- [x] Tester prix decimal français, validation, CRUD, duplication à nouvel ID, filtre et corbeille.
```csharp
store.Create(profile);
store.Update(profile with { PricePerKg = 24.90m });
Assert.Equal(24.90m, new FilamentStore(root).LoadAll().Single().PricePerKg);
```
- [x] Implémenter liste et formulaire, marque/nom/type/prix/abrasif, filtrage.
- [x] Vérifier tests ciblés ; revue dédiée ; ajouter la navigation à l’intégration.

### Task 3: Fiches fournisseurs, modèles et produits
**Files:** Créer `Core/Collections/CollectionProfile.cs`, `CollectionProfileStore.cs`, modifier `CollectionCatalog.cs`, créer `App/Views/CollectionEditorWindow.xaml(.cs)` ; tests associés.
**Produces:** `CollectionProfile` identifié, `CollectionProfileStore(string root)` CRUD et chemins relatifs. Catalogue conservant les dossiers sans fiche et retrouvant les fiches déplacées.
- [x] Tester adoption d’un dossier existant et visibilité après déplacement.
```csharp
Assert.Contains(catalog.Load(root, "06_FOURNISSEURS"), item => item.Path == movedPath);
```
- [x] Implémenter métadonnées et formulaire avec champs fournisseur adaptés.
- [x] Vérifier tests ciblés ; revue dédiée ; intégrer les actions de fiche.

### Task 4: Gestion clients/projets et interface commune
**Files:** Créer `Core/Workspace/EntityManagementService.cs`, `App/MainWindow.Management.cs`, `App/Controls/EntityActions.xaml(.cs)`, `FileManagement.cs`, `TrashView.xaml(.cs)` ; adapter profils/catalogues et vues.
**Consumes:** Les trois tâches précédentes.
- [x] Tester changements de noms et chemins sans changements d’ID/code/référence ; parent client déplacé ; changement de client ; restauration complète.
```csharp
Assert.Equal(original.Reference, profiles.LoadAll().Single().Reference);
Assert.Equal(destinationClient.Id, profiles.LoadAll().Single().ClientId);
```
- [x] Ajouter les actions communes et confirmations ; conserver la protection des modifications non enregistrées.
- [x] Relier Filaments et Corbeille à MainWindow et au rafraîchissement.
- [x] Vérifier suite complète, rendus WPF, revue globale et publier sous le nom fixe.

## Ledger

- Plan validé : architecture et implémentation autorisées par l’utilisateur.
- Intégration dans la branche `feat/full-management-filaments` du dépôt existant.

- Validation finale : 364/364 tests réussis, aucun test ignoré.
- Revues croisées : moteur/corbeille, filaments, fiches collections, intégration.
  Corrections vérifiées : références réservées après déplacement/corbeille,
  visibilité des dossiers historiques avec documents, sauvegardes et verrous
  contrôlés avant déplacement, récupération des journaux interrompus,
  commandes idempotentes et contraste AMOLED.
- Rendus WPF : 36 captures sous outputs/management-previews-verified avec les
  thèmes blanc, gris, AMOLED et papier ; contrôles fournisseurs, filaments,
  corbeille et formulaires, ressources réelles de l’application.
- Publication autonome Windows x64 réussie avec PortableWinX64.
- Données de tests exclusivement temporaires ou fixtures outputs ignorées.

- Livraison en attente : remplacement bloqué par une instance utilisant le binaire. L’ancienne version est sauvegardée dans outputs/backups ; le binaire neuf est préparé et vérifié, fermeture normale demandée. Aucun arrêt forcé.
