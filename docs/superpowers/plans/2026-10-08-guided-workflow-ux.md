# Parcours guidé client-projet-fichiers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** permettre de créer un client, un projet et de travailler dans les fichiers par un parcours court, réversible et lisible.

**Architecture:** les services Core actuels restent la source de vérité pour clients, projets, références, fichiers et corbeille. Un état d'assistant WPF conservera la sélection client et le brouillon entre étapes. Les lots ergonomiques restent séparés afin d'être vérifiables et livrables indépendamment.

**Tech Stack:** .NET 8, WPF, MaterialDesignInXAML, MS3DPRINT.Manager.Core, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-08-guided-workflow-ux-design.md`

## Global Constraints

- Préserver références, fiches, arborescences, corbeille et protections de chemins.
- Ne créer aucune donnée par consultation ou annulation.
- Écrire chaque comportement avec un test RED puis un correctif minimal GREEN.
- Préserver les thèmes clair, gris, AMOLED et papier, y compris à 700 px de largeur.
- Sérialiser builds, tests et publications ; chaque commande .NET produit un binlog.
- Livrer uniquement `MS3DPRINT Manager.exe`, avec SHA256 identique au binaire publié et un seul exe dans le dossier de livraison.

## Files and responsibilities

| Fichier | Responsabilité |
|---|---|
| `ViewModels/ProjectCreationFlow.cs` | État des étapes, brouillon et sélection client. |
| `Views/CreateTrackedProjectWindow.*` | Assistant projet et choix final. |
| `Views/CreateClientWindow.*` | Continuité client vers projet. |
| `MainWindow.xaml.cs`, `MainWindow.Management.cs` | Navigation et ouverture des parcours. |
| `Views/ProjectDetailView.*`, `Controls/FileManagement.cs` | Fichiers, import classé, 3D et disposition. |
| `Controls/WorkspaceFilesView.cs`, `Controls/CompactTable.cs` | Fil d'Ariane, tailles et défilement. |
| `MainWindow.xaml`, `App.xaml` | Accueil compact et animations. |

### Task 1: État de l'assistant

**Files:** Create `src/MS3DPRINT.Manager.App/ViewModels/ProjectCreationFlow.cs`; Modify `Views/CreateTrackedProjectWindow.xaml.cs`; Test `tests/MS3DPRINT.Manager.Core.Tests/Views/ProjectCreationFlowTests.cs`.

- [ ] Écrire d'abord un test qui instancie `ProjectCreationFlow`, sélectionne un `ClientSummary` avec fiche complète, passe à l'étape Projet et vérifie que `ProjectName`, année, échéance et description sont conservés.
- [ ] Exécuter `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter FullyQualifiedName~ProjectCreationFlowTests '-bl:outputs/logs/project-flow-red-{}.binlog'` et vérifier l'échec attendu car le type n'existe pas.
- [ ] Implémenter `ProjectCreationFlow` avec `Step`, `SelectedClient`, `Year`, `ProjectName`, `DueDate`, `Description`, `SelectClient`, `MoveToClient`, `MoveToProject`, `MoveToConfirmation`, `CanContinue` et `CanCreate`.
- [ ] Rejouer le test ciblé puis committer `feat: add project creation flow state`.

### Task 2: Assistant depuis Nouveau projet

**Files:** Modify `Views/CreateTrackedProjectWindow.xaml`, `Views/CreateTrackedProjectWindow.xaml.cs`, `MainWindow.xaml.cs`; Test `tests/MS3DPRINT.Manager.Core.Tests/Views/CreateTrackedProjectWindowTests.cs`.

- [ ] Écrire un test WPF qui confirme : Suivant est désactivé sans client, activé après sélection, et la création aboutit à un écran proposant Ouvrir le projet ou Retour à l'accueil.
- [ ] Exécuter `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter FullyQualifiedName~CreateTrackedProjectWindowTests '-bl:outputs/logs/project-wizard-red-{}.binlog'` et vérifier l'échec attendu.
- [ ] Remplacer le long formulaire par les panneaux Client, Projet et Confirmation avec indicateur d'étape, Précédent, Suivant et Annuler. Ajouter Nouveau client ; après retour du dialogue client, recharger le catalogue, pré-sélectionner le client et conserver le brouillon.
- [ ] Router les deux choix finaux via MainWindow, rejouer les tests ciblés puis committer `feat: guide client and project creation`.

### Task 3: Continuité depuis Nouveau client

**Files:** Modify `Views/CreateClientWindow.xaml`, `Views/CreateClientWindow.xaml.cs`, `MainWindow.xaml.cs`; Test `tests/MS3DPRINT.Manager.Core.Tests/Clients/CreateClientWindowSmokeTests.cs`.

- [ ] Écrire un test qui crée un client, vérifie les boutons Créer un projet maintenant et Retour à l'accueil, puis vérifie que le client transmis au projet a le même chemin et le même identifiant.
- [ ] Exécuter `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter FullyQualifiedName~CreateClientWindowSmokeTests '-bl:outputs/logs/client-continuation-red-{}.binlog'` et vérifier l'échec attendu.
- [ ] Après une première création réussie seulement, afficher les deux actions. La première expose le client créé à MainWindow, qui ouvre l'assistant de Task 2 avec ce client pré-sélectionné. L'édition reste inchangée.
- [ ] Rejouer le test ciblé puis committer `feat: continue from client to project`.

### Task 4: Travailler dans les fichiers du projet

**Files:** Modify `Views/ProjectDetailView.*`, `Controls/FileManagement.cs`, `Controls/WorkspaceFilesView.cs`; Create `tests/MS3DPRINT.Manager.Core.Tests/Views/ProjectDetailWorkflowTests.cs`; Modify `Views/FileManagementSelectionTests.cs`.

- [ ] Écrire un test qui importe deux fichiers temporaires vers `03_CAO_3D/03_STL` et vérifie leur présence, puis un test qui sélectionne un STL et vérifie la route explicite vers le visualiseur 3D sans ouverture lors de la seule sélection.
- [ ] Exécuter `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~FileManagementSelectionTests|FullyQualifiedName~ProjectDetailWorkflowTests" '-bl:outputs/logs/project-files-red-{}.binlog'` et vérifier l'échec attendu.
- [ ] Ajouter Importer et classer des fichiers : sélection multiple des sources, un seul choix de dossier de destination projet, import via le service géré existant. Conserver ouvrir, renommer, déplacer, supprimer et l'aperçu 3D.
- [ ] Recomposer l'en-tête projet, les onglets courts et la carte Informations ; rejouer les tests ciblés puis committer `feat: streamline project file work`.

### Task 5: Navigation, tailles et barres de défilement

**Files:** Modify `Controls/FileManagement.cs`, `Controls/WorkspaceFilesView.cs`, `Controls/CompactTable.cs`; Create `src/MS3DPRINT.Manager.Core/Files/FolderSizeService.cs`; Test `Files/ProjectFileBrowserTests.cs`, `Views/ResponsiveLayoutTests.cs`.

- [ ] Écrire un test avec des fichiers imbriqués totalisant 1536 octets, qui appelle `FolderSizeService.GetSizeAsync` et vérifie le total ; écrire aussi un test de largeur étroite qui vérifie une barre horizontale uniquement pour les colonnes débordantes.
- [ ] Exécuter `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~ProjectFileBrowserTests|FullyQualifiedName~ResponsiveLayoutTests" '-bl:outputs/logs/navigator-red-{}.binlog'` et vérifier l'échec attendu.
- [ ] Implémenter une taille asynchrone annulable et sûre sous la racine ; afficher un marqueur pendant le calcul. Remplacer le chemin complet par un fil d'Ariane, grouper la barre d'outils et supprimer les défilements imbriqués.
- [ ] Rejouer les tests ciblés puis committer `feat: improve project navigation and folder sizes`.

### Task 6: Accueil et animations utiles

**Files:** Modify `MainWindow.xaml`, `MainWindow.xaml.cs`, `App.xaml`; Test `tests/MS3DPRINT.Manager.Core.Tests/AppMarkupTests.cs`, `WindowLaunchSizeTests.cs`.

- [ ] Écrire un test de balisage qui exige Créer un client, Créer un projet, Projets récents et Alertes, puis rejette `ActionCardButton`.
- [ ] Exécuter `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~AppMarkupTests|FullyQualifiedName~WindowLaunchSizeTests" '-bl:outputs/logs/dashboard-red-{}.binlog'` et vérifier l'échec attendu.
- [ ] Remplacer les cartes décoratives par les actions, les projets et les alertes. Ajouter des Storyboards courts uniquement pour étapes, détails et confirmations, sans temporiser les commandes.
- [ ] Rejouer les tests ciblés puis committer `feat: simplify dashboard and transitions`.

### Task 7: Intégration et livraison

**Files:** Modify `docs/audits/2026-10-08-pages-ux-crud.md`, this plan.

- [ ] Exécuter `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --disable-build-servers '-bl:outputs/logs/guided-workflow-full-{}.binlog'` et obtenir zéro échec et zéro test ignoré.
- [ ] Capturer assistant, fiche projet, navigateur, tailles et accueil dans les quatre thèmes à 700 px et 1180 px ; vérifier les actions, animations et barres visibles.
- [ ] Publier avec `dotnet publish src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj --disable-build-servers -p:PublishProfile=PortableWinX64 -o outputs/MS3DPRINT-Manager '-bl:outputs/logs/guided-workflow-publish-{}.binlog'`.
- [ ] Remplacer uniquement `MS3DPRINT Manager.exe`, comparer les SHA256, vérifier qu'un seul exe est présent, documenter les preuves et committer `docs: record guided workflow delivery`.

## Self-review

Les Tasks 1 à 3 couvrent les deux entrées de création. Tasks 4 et 5 couvrent fichiers, 3D, import, tailles, navigation et défilement. Task 6 couvre l'accueil et les animations. Task 7 couvre la validation et la livraison. Chaque lot a son test RED, sa vérification GREEN, ses fichiers précis et sa frontière de commit.
