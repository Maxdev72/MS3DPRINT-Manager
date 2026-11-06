# Atelier unifié MS3DForge / Nextcloud — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Exécution dans la branche existante, sans délégation ni création de worktree implicites.

**Goal:** Retrouver un atelier unique sur deux PC et Android, avec documents Nextcloud réellement disponibles localement et données métier centralisées.

**Architecture:** API MS3DForge pour les fiches, client Nextcloud officiel pour les octets sur PC, connecteur serveur pour l'index et les accès documentaires web/mobile. Une identité d'atelier relie ces composants ; une projection locale ne constitue pas une deuxième base métier active.

**Tech Stack:** .NET 10, Avalonia 12.1.4, ASP.NET Core, stores JSON existants, Microsoft.Data.Sqlite déjà présent, WebDAV Nextcloud, coffres OS.

**Spec:** `docs/superpowers/specs/2026-11-06-atelier-unifie-nextcloud-design.md` et `docs/atelier-unifie-nextcloud.md`.

## Global Constraints

- .NET 10, Avalonia 12.1.4 et ASP.NET Core existants ; Windows x64, Ubuntu/Linux Mint, Android existant.
- Nom de lancement Windows : `MS3DPRINT Manager.exe`.
- Aucun document métier, secret ou cache dans Git ou dans le dossier du programme.
- Copie documentaire complète sur chaque PC déclaré prêt ; des placeholders ne constituent pas une copie complète.
- Métier hors connexion en consultation dans cette première version ; pas de file générique d'écritures métier hors connexion.
- Compte propriétaire actuel comme premier périmètre ; aucune inscription publique avant isolation des comptes et ateliers.
- Les services MS3DPRINT-FR et MS3DPRINT-SHOP restent hors périmètre.

## Découpage et dépendances

Ce chantier comprend plusieurs sous-systèmes. Exécuter les lots dans l'ordre ; chaque lot possède sa recette et son commit. Une livraison intermédiaire indique précisément ses capacités. Ne pas annoncer le chantier complet terminé après le seul lot de connexion.

| Lot | Dépend de | Livrable vérifiable |
|---|---|---|
| 1 | Aucun | Identités et emplacements stables |
| 2 | 1 | Descripteur/capacités d'atelier authentifiés |
| 3 | 1–2 | Démarrage guidé, session et consultation hors connexion |
| 4 | 1–2 | Connecteur Nextcloud et association des miroirs PC |
| 5 | 3–4 | Index documentaire, opérations et états fiables |
| 6 | 2–5 | Modules métier portés, stores locaux inactifs en mode unifié |
| 7 | 5–6 | Documents web/Android et sélection hors connexion |
| 8 | 1–7 | Migration sans nouvelle copie intégrale des documents |
| 9 | 1–8 | Recette, sauvegarde/restauration et déploiement |

Les tâches de lecture de configuration s'effectuent avant toute écriture réelle. Relever URL/version Nextcloud, partage, compte technique, connectivité depuis MS3DForge et hors réseau local, versions des clients et quotas. Une information manquante arrête le raccordement réel, pas les tests sur ateliers fictifs.

## Règle de validation et journal de travail

Créer `outputs/atelier-unifie/` localement, hors Git. Pour chaque changement : test de reproduction rouge, correction, test vert, revue du diff, commit limité au lot. Les exemples ci-dessous sont des tests à écrire ; aucun n'est présenté comme déjà exécuté.

Capturer un binlog pour chaque `dotnet build`, `test`, `publish` ou `restore` ; sous PowerShell, passer par exemple `'-bl:outputs/atelier-unifie/lot1-{}.binlog'`. Ne pas exécuter deux compilations de la même configuration dans les mêmes dossiers. Pour Linux, capturer aussi les journaux dans le profil de test WSL/Ubuntu.

## Lot 1 — Identités et emplacements stables

**Modifier :** `src/MS3DPRINT.Manager.Core/Storage/ApplicationPaths.cs`, `Storage/WorkspaceLocation.cs`, `Workspace/WorkspaceMetadataPaths.cs`.

**Créer :** `src/MS3DPRINT.Manager.Core/Remote/WorkspaceIdentity.cs`, `Storage/AccountWorkspacePaths.cs`, `Storage/LegacySettingsMigration.cs` ; tests `tests/MS3DPRINT.Manager.Core.Tests/Storage/AccountWorkspacePathsTests.cs` et `LegacySettingsMigrationTests.cs`.

**Interfaces produites :**

```csharp
public sealed record WorkspaceIdentity(Uri Server, string AccountId, Guid WorkspaceId);
public sealed class AccountWorkspacePaths {
    public string DirectoryPath { get; }
    public string CachePath => Path.Combine(DirectoryPath, "copy.json");
    public string BindingPath => Path.Combine(DirectoryPath, "binding.json");
    public string RecoveryDirectory => Path.Combine(DirectoryPath, "recovery");
    public AccountWorkspacePaths(string privateRoot, WorkspaceIdentity identity) {
        var server = identity.Server;
        if (!server.IsAbsoluteUri || server.Scheme != "https" || server.AbsolutePath != "/"
            || server.UserInfo.Length != 0 || server.Query.Length != 0 || server.Fragment.Length != 0
            || string.IsNullOrWhiteSpace(identity.AccountId) || identity.WorkspaceId == Guid.Empty)
            throw new ArgumentException("Identité d'atelier invalide.");
        var value = server.AbsoluteUri + "\0" + identity.AccountId + "\0" + identity.WorkspaceId.ToString("N");
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)));
        DirectoryPath = Path.Combine(Path.GetFullPath(privateRoot), "workspaces", key);
    }
}
```

La partition de disque est un SHA256 de l'origine canonique, du compte et du GUID d'atelier, séparés par un caractère nul. Ne pas utiliser le nom affiché de l'atelier comme clé de cache. Un constructeur à racine de métadonnées explicite est ajouté à `WorkspaceMetadataPaths`, sans retirer l'ancien contrat.

- [ ] Écrire les tests : même identité/même partition ; autre compte ou serveur/autre partition ; déplacement de l'exécutable/préférences inchangées ; URI HTTP et GUID nul refusés.

```csharp
var first = new AccountWorkspacePaths(root, new(new Uri("https://one.example"), "owner", id));
var other = new AccountWorkspacePaths(root, new(new Uri("https://one.example"), "other", id));
Assert.NotEqual(first.CachePath, other.CachePath);
Assert.StartsWith(Path.GetFullPath(root), first.DirectoryPath);
```

- [ ] Exécuter le filtre `FullyQualifiedName~AccountWorkspacePaths|FullyQualifiedName~LegacySettingsMigration` ; constater l'échec avant implémentation.
- [ ] Implémenter les chemins privés et la migration des réglages voisins du programme : sauvegarde, validation, import seulement en absence de réglages stables ; ne jamais importer des fiches métier par cette migration de préférences.
- [ ] Vérifier les tests, ainsi que `WorkspaceLocationTests`, Windows/Linux et les coffres existants. Refuser l'écrasement d'une association d'un autre atelier.
- [ ] Commit : `feat: persist account workspace settings independently of installation`.

## Lot 2 — Atelier et capacités côté serveur

**Créer :** `Core/Remote/WorkspaceDescriptor.cs`, `Server/Workspace/WorkspaceRegistry.cs`, `WorkspaceIdentityEndpoints.cs` ; test `Server.Tests/WorkspaceIdentityApiTests.cs`.

**Modifier :** `Server/Workspace/WorkspaceEndpoints.cs`, `Server/Authentication/AuthEndpoints.cs`, `Server/Workspace/CommandStore.cs`, `Core/Remote/RemoteWorkspaceClient.cs`.

Les chemins préfixés `Core`, `Server`, `Desktop` ou `Android` désignent les dossiers `src/MS3DPRINT.Manager.*` correspondants. `Core.Tests` et `Server.Tests` désignent respectivement `tests/MS3DPRINT.Manager.Core.Tests` et `tests/MS3DPRINT.Manager.Server.Tests`.

**Interfaces produites :** `WorkspaceDescriptor` et `DocumentStorageDescriptor` de la conception ; `RemoteWorkspaceClient.GetWorkspaceAsync(CancellationToken)` et `GetWorkspaceCapabilitiesAsync(CancellationToken)`. Les capacités énumèrent les modules réellement disponibles.

```csharp
using var host = new ServerFixture();
using var anonymous = host.Browser();
Assert.Equal(HttpStatusCode.Unauthorized,
    (await anonymous.GetAsync("/api/v1/workspace")).StatusCode);
using var owner = await host.AuthenticatedClient();
var one = await owner.GetFromJsonAsync<WorkspaceDescriptor>("/api/v1/workspace");
var two = await owner.GetFromJsonAsync<WorkspaceDescriptor>("/api/v1/workspace");
Assert.NotNull(one);
Assert.Equal(one.WorkspaceId, two!.WorkspaceId);
Assert.NotEqual(Guid.Empty, one.WorkspaceId);
```

- [ ] Écrire les tests d'identité persistante après redémarrage, refus anonyme et absence de secret/chemin hôte dans les réponses.
- [ ] Exécuter le filtre `FullyQualifiedName~WorkspaceIdentityApiTests` ; constater l'échec.
- [ ] Persister l'identité dans le registre privé. Résoudre le compte depuis l'authentification, jamais depuis un paramètre `accountId` fourni dans l'URL. Ajouter les routes et méthodes clientes ci-dessus.
- [ ] Publier une révision seulement lorsqu'elle est vérifiée avec les commandes acquittées. Tester coupure entre JSON et reçu : état incertain, écritures bloquées et aucun badge « à jour ». Le reçu reste consultable.
- [ ] Vérifier la compatibilité des anciennes routes et des commandes Pending ; commit `feat: expose authenticated workspace identity and capabilities`.

## Lot 3 — Connexion et atelier au démarrage

**Créer :** `Desktop/WorkspaceStartupPolicy.cs`, `MainWindow.Onboarding.cs`, `Core/Remote/WorkspaceBindingStore.cs` ; tests `Core.Tests/WorkspaceStartupPolicyTests.cs` et `WorkspaceOnboardingUiTests.cs`.

**Modifier :** `Desktop/MainWindow.Remote.cs`, `MainWindow.axaml.cs`, `MainWindow.axaml`, `Core/Remote/RemoteWorkspaceConnection.cs`, `RemoteWorkspaceCache.cs`.

**Interfaces :**

```csharp
public enum WorkspaceStartupState {
    NeedsSignIn, NeedsWorkspaceSelection, NeedsDocumentsBinding,
    HydratingDocuments, ReadyOnline, ReadyOfflineReadOnly, RecoveryRequired
}
public sealed record StartupFacts(bool HasAuthenticatedSession, bool NetworkAvailable,
    bool HasVerifiedCopy, bool WorkspaceSelected, bool DocumentsBound,
    bool LocalMirrorComplete, bool RecoveryRequired);
public static class WorkspaceStartupPolicy {
    public static WorkspaceStartupState Resolve(StartupFacts facts) {
        if (facts.RecoveryRequired) return WorkspaceStartupState.RecoveryRequired;
        if (!facts.NetworkAvailable) return facts.HasVerifiedCopy
            ? WorkspaceStartupState.ReadyOfflineReadOnly : WorkspaceStartupState.NeedsSignIn;
        if (!facts.HasAuthenticatedSession) return WorkspaceStartupState.NeedsSignIn;
        if (!facts.WorkspaceSelected) return WorkspaceStartupState.NeedsWorkspaceSelection;
        if (!facts.DocumentsBound) return WorkspaceStartupState.NeedsDocumentsBinding;
        return facts.LocalMirrorComplete ? WorkspaceStartupState.ReadyOnline
            : WorkspaceStartupState.HydratingDocuments;
    }
}
```

L'identité de la copie et l'autorisation locale de consultation sont vérifiées avant de renseigner `HasVerifiedCopy`. Une déconnexion explicite retire cette autorisation jusqu'à une nouvelle connexion, même si des fichiers de cache sont encore présents. Le mode unifié complet est conditionné par les capacités serveur/client ; une installation encore locale passe par le parcours de reprise, pas par un écrasement silencieux.

```csharp
var state = WorkspaceStartupPolicy.Resolve(new(false, false, true, true, true, true, false));
Assert.Equal(WorkspaceStartupState.ReadyOfflineReadOnly, state);
var fresh = WorkspaceStartupPolicy.Resolve(new(false, false, false, false, false, false, false));
Assert.Equal(WorkspaceStartupState.NeedsSignIn, fresh);
```

- [ ] Écrire les tests de session mémorisée, expiration, appareil neuf hors réseau, mauvais compte, copie illisible, déconnexion suivie d'un redémarrage hors ligne et fermeture du parcours sans modification de données.
- [ ] Exécuter les filtres `WorkspaceStartupPolicyTests` et `WorkspaceOnboardingUiTests` ; constater l'échec.
- [ ] Implémenter la politique et réutiliser les contrôles de connexion existants. Si un seul atelier est autorisé, le sélectionner automatiquement. Refuser le repli en écriture locale après une panne.
- [ ] Tester « Hydratation » : fiches disponibles, fichiers manquants identifiés, aucun faux état prêt. Tester qu'une ancienne copie locale est signalée comme reprise non terminée et n'est pas annoncée partagée.
- [ ] Vérifier focus clavier, libellés, tailles de fenêtre et aucun secret journalisé ; commit `feat: guide sign in and workspace restoration at startup`.

## Lot 4 — Nextcloud et miroir local vérifié

**Créer :** `Server/Documents/NextcloudDocumentProvider.cs`, `NextcloudStorageBindingStore.cs`, `NextcloudSecretStore.cs`, `Core/Documents/WorkspaceDocumentMarker.cs`, `LocalDocumentMirror.cs` ; tests `Server.Tests/NextcloudDocumentProviderTests.cs` et `Core.Tests/LocalDocumentMirrorTests.cs`.

**Modifier :** `Core/Storage/SyncFolderDetector.cs`, `Desktop/MainWindow.Onboarding.cs`.

**Interfaces produites :** `DocumentStorageDescriptor`, `LocalWorkspaceBinding` et `DocumentLink` de la conception ; `MirrorAvailability { Complete, Incomplete, Unknown }` ; `LocalDocumentMirror.CheckAsync(LocalWorkspaceBinding, CancellationToken)` retourne disponibilité, nombres vérifiés et anomalies.

Le fournisseur expose `ProbeRootAsync`, `ListAsync`, `OpenReadAsync` et `EnsureDirectoryAsync`, tous avec annulation. Ses réponses contiennent identité opaque, ETag, taille et chemin relatif. Le transport HTTP et le secret sont injectés au constructeur, jamais acceptés comme destination arbitraire dans une requête utilisateur.

```csharp
public sealed record NextcloudRootInfo(string InstanceId, string RootFileId,
    bool CanRead, bool CanWrite);
public sealed record NextcloudEntry(string ProviderFileId, string RelativePath,
    string Etag, long Bytes, bool IsDirectory);
public interface INextcloudDocumentProvider {
    Task<NextcloudRootInfo> ProbeRootAsync(CancellationToken token);
    Task<IReadOnlyList<NextcloudEntry>> ListAsync(string relativeDirectory, CancellationToken token);
    Task<Stream> OpenReadAsync(string relativePath, string expectedFileId,
        string expectedEtag, CancellationToken token);
    Task<NextcloudEntry> EnsureDirectoryAsync(Guid operationId, string relativePath,
        CancellationToken token);
}
```

```csharp
// Un répertoire ordinaire vide ne prouve pas l'existence d'un miroir.
var binding = new LocalWorkspaceBinding(workspaceId, "owner",
    new Uri("https://one.example"), storageId, emptyDirectory);
var result = await mirror.CheckAsync(binding, CancellationToken.None);
Assert.NotEqual(MirrorAvailability.Complete, result.Availability);
```

- [ ] Écrire les tests DAV avec un transport HTTP simulé : racine approuvée, mauvais compte, redirection externe, ID opaque, accès hors périmètre, dossier existant du mauvais type, annulation et 401.
- [ ] Écrire les tests miroir : marqueur conforme/mauvais atelier, dossier absent, fichier virtuel non hydraté, fichier illisible, comparaison Windows/Linux et chemins sortant de la racine.
- [ ] Exécuter les deux suites et constater les échecs ; implémenter l'autorisation et le connecteur. Garder les secrets dans le stockage chiffré et les sessions desktop dans leurs coffres respectifs.
- [ ] Implémenter détection puis sélecteur unique. Guider la copie complète via les réglages supportés de Nextcloud ; ne pas lire/modifier sa base interne ni passer de secret sur une ligne de commande.
- [ ] Faire une recette Nextcloud réelle sur atelier fictif, Windows fichiers virtuels et Linux ; commit `feat: bind verified Nextcloud document mirrors to workspaces`.

## Lot 5 — Index documentaire et opérations récupérables

**Créer :** `Server/Documents/DocumentIndex.cs`, `DocumentOperations.cs`, `DocumentVerificationWorker.cs`, `DocumentEndpoints.cs`, `Core/Remote/RemoteWorkspaceClient.Documents.cs`, `Core/Documents/WorkspaceSyncStatus.cs`, `Desktop/MainWindow.Documents.cs` ; tests `Server.Tests/DocumentApiTests.cs`, `DocumentOperationRecoveryTests.cs`, `Core.Tests/WorkspaceSyncStatusTests.cs`.

**Modifier :** `Server/Workspace/WorkspaceCommands.cs`, `Server/Workspace/WorkspaceReader.cs`, `Desktop/MainWindow.ProjectFiles.cs`.

**Interfaces produites :** routes du §9 de la conception ; tables `document_links` et `document_operations`, liées à `WorkspaceId` et `StorageBindingId`. Une clé unique déduplique chaque commande ; le même identifiant avec une requête différente est refusé.

```csharp
public sealed record SyncFacts(bool DataCurrent, bool LocalMirrorComplete,
    bool RemoteDocumentsVerified, bool HasUncertainCommand);
public static class WorkspaceSyncStatus {
    public static bool IsUpToDate(SyncFacts facts) => facts.DataCurrent
        && facts.LocalMirrorComplete && facts.RemoteDocumentsVerified
        && !facts.HasUncertainCommand;
}
```

```csharp
Assert.False(WorkspaceSyncStatus.IsUpToDate(new(true, true, false, false)));
Assert.False(WorkspaceSyncStatus.IsUpToDate(new(true, true, true, true)));
Assert.True(WorkspaceSyncStatus.IsUpToDate(new(true, true, true, false)));
```

- [ ] Écrire les tests : fichier présent uniquement localement, upload non confirmé, correspondance exacte du contenu distant, ETag traité comme opaque, conflit, suppression externe, renommage et mauvais projet.
- [ ] Écrire les tests de coupure avant/après création du dossier distant et avant/après persistance du reçu. Relancer l'inspection, pas une écriture aveugle. Exécuter les filtres `DocumentApiTests|DocumentOperationRecoveryTests|WorkspaceSyncStatusTests` pour constater les échecs.
- [ ] Implémenter l'index et les routes en flux authentifiées. Valider les IDs d'entité et les droits dans le contexte d'atelier. Pour l'upload mobile, annoncer une limite testée dans les capacités et conserver les petites limites métier globales.
- [ ] Effectuer les vérifications longues hors de la requête interactive, avec état durable et parallélisme borné. Si le fournisseur ne donne pas d'empreinte fiable, calculer le SHA256 en flux côté serveur une fois par version, avec contrôle d'ETag avant/après. Réutiliser ce résultat tant que la version ne change pas ; aucun téléchargement intégral répété à chaque rafraîchissement.
- [ ] Découpler la création métier de la création du dossier serveur : persister l'opération Nextcloud et sa récupération. Remplacer la dépendance de lecture aux répertoires MS3DForge par la lecture des fiches.
- [ ] Ajouter surveillance locale temporisée, rescan après reprise, confirmation distante et états visibles. Tester aussi des modifications faites dans l'Explorateur avec Manager fermé. Commit `feat: track document identity operations and verified sync status`.

## Lot 6 — Tous les modules sur la référence serveur

Livrer un sous-lot par domaine. Les sous-lots suivent la même règle de reçus/versions mais chaque schéma garde ses validateurs. Ajouter uniquement les capacités effectivement livrées. Chaque sous-lot doit passer ses tests avant le suivant.

| Sous-lot | Sources précises | Nouveaux points d'entrée | Recette spécifique |
|---|---|---|---|
| 6a | `Core/Filaments/FilamentStore.cs`, `Core/Printers/PrinterStore.cs` | `Server/Workspace/EquipmentEndpoints.cs`, `Desktop/MainWindow.RemoteEquipment.cs` | Valeurs décimales, unités, versions et suppression confirmée |
| 6b | `Core/Estimates/EstimateCalculator.cs`, `WorkshopEstimateSettingsStore.cs` | `Server/Workspace/EstimateEndpoints.cs`, `Desktop/MainWindow.RemoteEstimates.cs` | Calculs identiques, révisions de devis et PDF séparé |
| 6c | `Core/Purchases/PurchaseJournal.cs`, `ExpensePlanStore.cs`, `Core/Revenues/RevenueJournal.cs` | `Server/Workspace/FinanceEndpoints.cs`, `Desktop/MainWindow.RemoteFinance.cs` | Corrections append-only, historique, doublons et reçus conservés |
| 6d | `Core/Collections/CollectionProfileStore.cs` | `Server/Workspace/CollectionEndpoints.cs`, `Desktop/MainWindow.RemoteCollections.cs` | Modèles documentaires accessibles sans chemin hôte |
| 6e | `Core/Templates/ProjectTemplateStore.cs`, registres de numérotation, réglages d'atelier | `Server/Workspace/WorkshopSettingsEndpoints.cs` | Pas de duplication de références ni de paramètres machine partagés |
| 6f | `Core/Workspace/ManagedFileService.cs`, `EntityManagementService.cs`, `ClientCodeChanger.cs`, `Core/Files/ProjectFileClassifier.cs` | `Server/Documents/DocumentOperations.cs` | Déplacement/suppression suivis, confirmation et récupération |

Les requêtes des nouveaux domaines vont dans des fichiers `Core/Remote/*Contracts.cs` spécifiques. Les pages existantes restent dans leur partiel concerné ; ne pas recopier toute la fenêtre principale.

Exemple de commande typée pour 6a, sans accepter un JSON ou un chemin arbitraire :

```csharp
public sealed record UpdateFilamentCommand(Guid CommandId,
    DateTimeOffset ExpectedUpdatedAt, string Brand, string Name,
    string Material, decimal PricePerKg, bool IsAbrasive, string? Color);
```

L'ID vient de la route, les dates techniques et droits du contexte serveur. Réutiliser les validations du store ; étendre ses contrôles de version lorsque le store ne dispose pas encore de l'overload nécessaire. Ne pas laisser le client imposer `CreatedAt` ou le résultat d'un calcul financier.

- [ ] Pour chaque sous-lot, écrire `EquipmentApiTests`, `EstimateApiTests`, `FinanceApiTests`, `CollectionApiTests`, `WorkshopSettingsApiTests` ou `DocumentClassificationTests` dans les tests serveur. Couvrir validation, concurrence, refus anonyme, accès objet et reçu incertain.
- [ ] Ajouter le test UI correspondant dans `Core.Tests` : après modification distante, l'ancien fichier local reste octet pour octet inchangé. Utiliser un atelier local sentinelle dans chaque fixture.
- [ ] Exécuter le filtre du domaine pour constater l'échec ; implémenter DTO, commande serveur, lecture, projection et adaptateur de page. Conserver exports, calculs, reconnaissance et ouverture des fichiers comme fonctions locales utilisant les bonnes données d'entrée.
- [ ] Vérifier création/modification depuis deux clients : une version obsolète est refusée, sans perdre la saisie. Tester les corrections comptables avec les tests de journaux existants, sans remplacer leur historique par un simple CRUD.
- [ ] Commit de chaque sous-lot : `feat: use server workspace for <domain>`. Un dernier test `UnifiedWorkspaceRoutingTests` parcourt les modules et interdit tout accès en écriture à l'ancien atelier quand le mode unifié est actif.

## Lot 7 — Web et Android, documents réellement hors connexion

**Modifier :** `Server/wwwroot/app.js`, `index.html`, `app.css`, `Android/MainView.cs`, `Services/LocalStore.cs`, `Services/ShareInbox.cs`, `Pages/ProjectsPage.cs`, `Pages/SettingsPage.cs`, les vues `Desktop/Remote/RemoteWorkspaceView*.cs` utiles.

**Créer :** `Android/Services/DocumentOfflineStore.cs`, `DocumentOpenService.cs`, `Android/Pages/ProjectDocumentsPage.cs`. Les ports de téléchargement restent dans `Core/Remote/RemoteWorkspaceClient.Documents.cs` ; les URI et autorisations Android restent dans l'hôte.

**Interfaces produites :** sélection de projets/documents hors connexion, état de téléchargement vérifié, ouverture par une application associée. Le stockage conserve `DocumentId`, `StorageBindingId`, version, empreinte et emplacement persistant/URI autorisée.

- [ ] Ajouter des tests de stockage portable dans `Core.Tests/DocumentOfflineManifestTests.cs` : fichier partiel jamais déclaré prêt, empreinte différente refusée, versions distinctes conservées, retrait local sans suppression distante.
- [ ] Écrire une recette Android instrumentée : télécharger un PDF, fermer l'application, activer le mode avion, relancer et ouvrir réellement le PDF dans une application associée. Rejouer après redémarrage du téléphone.
- [ ] Implémenter téléchargement atomique vérifié et autorisations via le sélecteur système. Pour un accès indépendant depuis le gestionnaire de fichiers, sélectionner un dossier réellement local du téléphone. Une URI du fournisseur cloud n'est pas une preuve de copie locale. Ne pas utiliser un simple cache temporaire ou un chemin `/storage` supposé universel. Prévoir une capacité disque insuffisante et une autorisation révoquée.
- [ ] Ajouter liste documentaire et téléchargement authentifié sur le web. Pas de lien public permanent. La reprise en ligne revalide la version ; toute écriture mobile utilise la version attendue et un reçu durable.
- [ ] Préserver les captures/photos/brouillons existants : un brouillon local est annoncé « à envoyer » et ne devient pas une écriture comptable confirmée hors connexion. Construire l'APK avec binlog et tester sur appareil réel ; commit `feat: access project documents on web and Android offline`.

## Lot 8 — Migration de l'atelier existant

**Modifier :** `Core/Migration/GuidedWorkspaceMigration.cs`, `PortableWorkspaceMigrationService.cs`, `WorkspaceReconciliationService.cs`, `Desktop/MainWindow.Migration.cs`, `Server/Migration/WorkspaceMigrationStore.cs`, `WorkspaceMigrationEndpoints.cs`.

**Créer :** `Core/Migration/WorkspaceAdoptionManifest.cs`, `Server/Documents/DocumentAssociationImporter.cs` ; tests `Server.Tests/WorkspaceAdoptionEndToEndTests.cs` et `Core.Tests/WorkspaceAdoptionUiTests.cs`.

**Interface produite :** manifeste de reprise versionné, avec source des fiches, empreintes, périmètre documentaire, correspondances d'IDs, exceptions, choix et reçu. Les chemins physiques restent dans le journal privé de l'appareil, pas dans les réponses publiques.

- [ ] Écrire le scénario avec fiches et documents dans deux emplacements distincts, plus une ancienne copie de développement. Vérifier que la source sélectionnée reste intacte et que la copie d'essai n'est pas fusionnée.
- [ ] Écrire les tests : documents déjà sur Nextcloud, données PC plus récentes que serveur, conflits, fichiers sans lien, collections sans dossier et dossiers historiques. Les identifiants et historiques doivent être conservés.
- [ ] Exécuter les deux suites pour constater les échecs. Préparer une copie privée, sauvegarder/vérifier les métadonnées et exiger une sauvegarde documentaire Nextcloud restaurable avant bascule. Ne pas imposer un nouvel upload intégral des fichiers corrects.
- [ ] Présenter le bilan et demander la confirmation finale. Persister l'intention avant toute bascule. Une réponse perdue impose la vérification du reçu ; des sources modifiées imposent une nouvelle comparaison.
- [ ] Après succès, associer les deux PC et le téléphone à l'identité d'atelier. Les anciens stores locaux restent en récupération/consultation, sans écriture par les nouveaux clients. Commit `feat: adopt existing workspaces with verified Nextcloud document links`.

## Lot 9 — Recette, restauration et livraison

**Créer :** `docs/recette-atelier-unifie-nextcloud.md`, `docs/restauration-atelier-unifie.md` et un reçu opérationnel hors Git dans `outputs/atelier-unifie/`.

```text
PC A enregistre une fiche -> confirmation MS3DForge -> PC B et Android retrouvent la version.
PC A crée un PDF -> disponible localement -> confirmation Nextcloud -> PC B/Android l'ouvrent.
Couper Internet et fermer Manager -> ouvrir les documents du PC dans l'Explorateur.
Couper Nextcloud -> garder la copie locale et afficher l'envoi en attente, jamais « à jour ».
```

- [ ] Exécuter la recette du §16 de la conception sur atelier fictif : Windows, Ubuntu/Mint, Android et navigateur. Consigner versions, racines différentes, preuves d'ouverture hors ligne et redémarrages. Tester aussi Android sur réseau mobile avec les deux PC éteints.
- [ ] Exécuter les suites Core et serveur, puis les tests Linux et Android appropriés. Construire l'image serveur, l'exécutable Windows unique et l'archive Linux. Comparer les empreintes publié/livré.
- [ ] Restaurer sur environnement isolé les sauvegardes MS3DForge et Nextcloud ; vérifier fiches, historique, identités, secrets récupérables et liens après changement éventuel d'IDs fournisseur.
- [ ] Préparer le serveur compatible sans activation générale. Sauvegarder les données réelles ; vérifier la configuration du seul compagnon. La bascule réelle attend le bilan et la confirmation du propriétaire.
- [ ] Vérifier le deuxième PC et Android après activation, sans toucher aux sites commerciaux. Mettre les documents fonctionnels à jour et consigner les limites réelles. Commit final de validation ; ne pas annoncer de synchronisation complète tant qu'un module ou une copie obligatoire manque.

## Vérifications transversales avant chaque fin de lot

Pas de secret, document réel ou dump utilisateur dans Git. Aucune racine documentaire choisie par nom seul. Aucune validation TLS désactivée. Aucun endpoint de lecture/écriture hors atelier. Aucun écrasement silencieux d'une version concurrente. Aucune confirmation destructrice déclenchée par Entrée par défaut. Aucun effacement du miroir documentaire à la déconnexion.

Les comptes clients futurs requièrent un chantier d'isolation vérifié avant inscription publique : tests d'accès croisé pour fiches, documents, opérations et sauvegardes. Le succès du propriétaire unique ne valide pas ce comportement multi-clients.

## Définition de terminé

Les six lots fonctionnels de la spécification sont couverts par les neuf lots techniques ci-dessus. Aucun lot d'implémentation n'est coché par la seule existence de ce document. La recette finale doit montrer le même atelier sur deux PC et Android, des documents PC accessibles sans Manager ni réseau, et des états qui ne masquent ni conflit ni transfert en attente.
