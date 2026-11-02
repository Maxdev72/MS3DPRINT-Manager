# Préparation de migration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task in this session.

**Goal:** Exporter un atelier de test vers une archive vérifiée et préparer sa
restauration isolée avec un bilan, sans modifier l'atelier actif.

**Architecture:** Réutiliser `WorkspaceBackupService` pour le ZIP, la validation
des chemins, les SHA256 et la restauration dans un dossier neuf. Ajouter un
inventaire avant/après export et un contrôle des métadonnées et relations métier.
L'import CLI écrit seulement dans un nouveau sous-dossier privé de préparation.

**Tech Stack:** .NET 10, Core, System.Text.Json, SHA256, xUnit, Docker Linux.

**Spec:** `docs/superpowers/specs/2026-11-02-acces-distant-design.md`.

## Contraintes

- Branche `feature/ms3dforge-remote`, dossier actuel ; données fictives uniquement.
- Aucun changement de l'atelier actif, aucun écrasement de destination existante.
- Aucun déploiement ou changement des projets Coolify MS3DPRINT-FR/MS3DPRINT-SHOP.
- Préserver les octets des documents et les identifiants ; pas de remplacement
  global des chemins. Les références absolues ou à adapter bloquent la préparation.
- Toute exclusion de données est indiquée dans le bilan. Un dossier hors
  périmètre de sauvegarde exige une décision avant une migration réelle.
- Cette tranche prépare et contrôle ; elle n'active pas un atelier distant et
  ne remplace pas les futurs contrôles de sauvegarde extérieure et de bascule.

## Fichiers

- `Core/Migration/MigrationReport.cs` : contrats du bilan et inventaire des fichiers.
- `Core/Migration/MigrationInventory.cs` : inventaire SHA256 des chemins gérés,
  exclusions et comparaison avant/après export.
- `Core/Migration/MigrationMetadataAudit.cs` : JSON, identifiants, relations et
  chemins portables ; signaler sans modifier les fichiers.
- `Core/Migration/WorkspaceMigrationService.cs` : orchestration export/préparation
  et rapport durable ; utilise `WorkspaceBackupService`.
- `Core/Storage/WorkspaceBackupService.cs` : rendre internes deux validateurs
  existants pour les réutiliser, sans changer leur comportement.
- `Server/MigrationCommands.cs`, `Server/Program.cs` : commandes CLI d'administration.
- `Server.Tests/MigrationTests.cs` et `MigrationCommandTests.cs` : parcours et refus.
- `docs/acces-distant-serveur.md` : commandes, rapports et limites.

Les chemins Core/Server/Server.Tests ci-dessus désignent respectivement
`src/MS3DPRINT.Manager.Core`, `src/MS3DPRINT.Manager.Server` et
`tests/MS3DPRINT.Manager.Server.Tests`.

### Task 1 — Export vérifié

**Interfaces :** `WorkspaceMigrationService.Export(string sourceRoot, string packageDirectory)`
renvoie `MigrationReport` et écrit `report.json`. Si les contrôles de copie
réussissent, `workspace.zip` est conservé avec son SHA256 dans le rapport.

- [x] Écrire un test créant un client, son projet et un document de test :

```csharp
var report = new WorkspaceMigrationService().Export(source, package);
Assert.True(report.Succeeded);
Assert.True(File.Exists(Path.Combine(package, "workspace.zip")));
Assert.NotNull(report.ArchiveSha256);
Assert.Contains(report.Files, f => f.Path.EndsWith("piece.stl"));
```

- [x] Exécuter les tests et constater l'absence du comportement.
- [x] Implémenter les contrats, l'inventaire et l'export. Les fichiers gérés sont
  ceux de la sauvegarde complète ; les entrées ignorées sont rapportées. Capturer
  contenu, taille et date, puis comparer après export. Refuser liens, collisions
  de casse, noms Windows interdits et journaux de classement en attente.
- [x] Ajouter les tests source inchangée, destination existante, destination
  sous la source, exclusions explicites et rapport de refus consultable.

### Task 2 — Préparation isolée et audit

**Interfaces :** `WorkspaceMigrationService.Stage(string archivePath, string preparationDirectory,
string expectedArchiveSha256)` restaure dans `preparationDirectory/workspace`.
Le dossier parent de préparation ne doit pas exister. `MigrationReport` expose
`Succeeded`, `ArchiveSha256`, `Files`, `Directories` et `Issues` (`Code`, `Path`,
`Message`, `Blocking`). Aucun champ ne change la racine configurée du serveur.

- [x] Écrire puis faire échouer les tests d'archive incomplète, archive altérée,
  mauvaise empreinte, client manquant pour un projet, JSON corrompu et référence
  absolue Windows. Vérifier que l'atelier source et la destination active restent intacts.
- [x] Utiliser Inspect/Restore du moteur existant. Refuser une archive sans
  documents. Après extraction, vérifier l'inventaire extrait contre les tailles
  et SHA256 du manifeste original. Conserver le dossier préparé pour diagnostic
  si l'audit échoue ; ne jamais le présenter comme actif.
- [x] Auditer les JSON de métadonnées, puis les fiches clients/projets : identifiants
  non vides et uniques, nom de fichier conforme, client référencé présent, dossier
  relatif sûr et existant. Les champs de chemin absolus ou utilisant un séparateur
  Windows sont signalés comme conversions nécessaires, sans réécriture.
- [x] Tester la conservation des documents, historiques, modules non portés et
  métadonnées utiles dans un aller-retour ; vérifier le rapport JSON sauvegardé.

### Task 3 — CLI et vérification

**Interfaces :** `MigrationCommands.Execute(string[] args, ServerStorage storage, TextWriter output)`
renvoie un code : 0 si le bilan est favorable, 2 si des contrôles refusent la
préparation, 1 si la commande est invalide. `--export-migration <nouveau-dossier>`
utilise la racine d'atelier configurée. `--stage-migration <archive> --sha256 <empreinte>`
crée une préparation unique sous `StateRoot/migrations`. Les commandes ne démarrent
aucun serveur HTTP et n'activent jamais une préparation.

- [x] Tester les arguments invalides et les parcours CLI avec des dossiers fictifs,
  puis implémenter le routage avant le démarrage HTTP dans Program.
- [x] Documenter les exclusions, refus, empreintes et le fait qu'une préparation
  n'est pas une activation. Aucune promesse de reprise réseau ou conversion de
  tous les anciens chemins dans cette tranche.
- [x] Exécuter les tests serveur sous Windows et Linux, ainsi que les tests
  existants de sauvegarde ; vérifier le diff et obtenir une revue indépendante.
- [x] Committer les seuls fichiers du lot après vérification.

## Vérifications du lot

- 39 tests serveur réussis sous Windows et dans la cible Docker Linux verify.
- Régression de la solution : 1 209 tests Core et 73 tests estimateur réussis ; les contrôles supplémentaires du migrateur sont dans la suite serveur.
- Commandes réelles export/préparation testées sur un atelier temporaire : codes 0, document SHA256 identique, racine active inchangée.
- Revue indépendante : cas des clients groupés reproduit puis corrigé, relecture ciblée sans autre constat.
- Limites assumées : pas de conversion des anciens chemins, d’activation ou de reprise réseau ; arrêt des écritures externes nécessaire pour une migration réelle.
