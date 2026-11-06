# Reprise guidée PC / serveur — plan d’implémentation

> Exécuter avec subagent-driven-development : tâches délimitées et revue avant livraison.

**Goal:** Assistant natif de sauvegarde, transfert privé, comparaison, choix,
activation vérifiée et reçu durable de reprise d’un atelier PC récent.

**Architecture:** Core compare et construit une candidate auditée. Le serveur
reçoit des morceaux et possède les opérations privées ainsi que la bascule
récupérable. Manager orchestre sauvegarde/portabilité et le parcours utilisateur.

**Tech Stack:** .NET10, ASP.NET Core, Avalonia, SQLite existant, JSON atomique,
SHA256 et ZIP existants. Aucune nouvelle dépendance.

**Spec:** `docs/superpowers/specs/2026-11-04-guided-workspace-reconciliation-design.md`.

## Global Constraints

- Ne modifier aucune donnée réelle ni production pendant le développement.
- Aucun contrôle du PC/navigateur/Coolify ; aucun nouveau port ni confiance réseau.
- MS3DPRINT-FR et MS3DPRINT-SHOP hors périmètre.
- Branche dédiée actuelle ; result.json préexistant reste intact.
- Source PC intacte ; copie et sauvegardes privées hors atelier, Nextcloud et Git.
- PC plus récent est un contexte de reprise, pas une autorisation d’écrasement silencieux.
- Serveur seul conservé ; conflit de contenu explicitement choisi PC ou serveur.
- Sauvegardes, empreintes et audits avant activation ; aucun rejeu Apply incertain.
- Exécutable unique livré : MS3DPRINT Manager.exe ; ancien uniquement outputs/backups.
- Binlog pour toute commande .NET ; coordonner les dossiers de build/test partagés.

## Interfaces communes fixées

Core/Migration :
- enum WorkspaceDifferenceKind { Same, PcOnly, ServerOnly, Conflict }
- enum WorkspaceConflictChoice { Pc, Server }
- WorkspaceSnapshot : Fingerprint, Files (MigrationFile[]), Directories (string[]),
  Issues (MigrationIssue[]), Labels (dictionnaire chemin/libellé optionnel).
- WorkspaceDifference : Path, Kind, Pc (MigrationFile?), Server (MigrationFile?), DisplayName.
- WorkspaceReconciliationPlan : PcFingerprint, ServerFingerprint, PlanHash, Files,
  Directories, Issues. Tous tableaux/listes en lecture seule dans le contrat.
- WorkspaceReconciliationChoice(string Path, WorkspaceConflictChoice Choice).
- WorkspaceVersionChange : Path, PreviousUpdatedAt, UpdatedAt, BeforeSha256, AfterSha256.
- WorkspaceReconciliationCandidate : Succeeded, Snapshot, VersionChanges, Issues.
- WorkspaceReconciliationService.Capture(string root), Compare(pcSnapshot,serverSnapshot),
  ReviewHash(plan,choices), BuildCandidate(pcRoot,serverRoot,newRoot,plan,choices).
Capture expose toujours les empreintes ; ses audits peuvent signaler une métadonnée
Windows à convertir. La stabilité de la source brute compare ses empreintes ;
seules les copies portables et la candidate doivent passer tous les audits.

Core/Remote/WorkspaceMigrationContracts.cs (propriétaire tâche2) :
- enum WorkspaceMigrationPhase { Uploading, Uploaded, Preparing, Prepared, Applying, Applied, Blocked }
- WorkspaceMigrationCapabilities(Version:int, ChunkBytes:int, MaxArchiveBytes:long).
- StartWorkspaceMigration(CommandId:Guid, Bytes:long, Sha256:string).
- WorkspaceMigrationReceipt : OperationId, CommandId, Fingerprint, AppliedAt,
  PcFingerprint, ServerBeforeFingerprint, BackupVerified (sans chemin serveur).
- WorkspaceMigrationStatus : OperationId, Phase, ArchiveBytes, ArchiveSha256,
  ChunkBytes, ReceivedParts(int[]), Plan?, Choices[], ReviewHash?, CandidateFingerprint?, Receipt?, Issues[].
- ApplyWorkspaceMigration(CommandId:Guid, ReviewHash:string, Confirmed:bool).
- GET /api/v1/workspace-migrations/capabilities
- POST /api/v1/workspace-migrations/operations
- GET /api/v1/workspace-migrations/operations/{id}
- PUT /api/v1/workspace-migrations/operations/{id}/parts/{index}, X-MS3DFORGE-SHA256
- POST /api/v1/workspace-migrations/operations/{id}/prepare
- PUT /api/v1/workspace-migrations/operations/{id}/choices (choix bornés, pas racine)
- POST /api/v1/workspace-migrations/operations/{id}/apply
Version1, ChunkBytes=524288, MaxArchiveBytes=4294967296.

### Task 1: Comparaison et candidate portables

Files : Core/Migration/WorkspaceReconciliation*.cs, PortableWorkspaceMigrationService.cs,
tests Core/Migration ou Server.Tests/ReconciliationTests.cs selon portabilité.

- [x] RED : même contenu/dates différentes => Same ; fichiers PC seuls ajoutés ;
  serveur seuls conservés ; conflit refuse une candidate sans choix ; choix explicite
  conserve les octets sélectionnés. Snapshot cible changé => refus.
- [x] Réutiliser MigrationInventory, ValidateRelative et audits ; empreinte canonique
  sur chemins/tailles/SHA/dossiers ; collision de casse/fichier-dossier bloquante.
- [x] Candidate neuve, copie et hash contrôlés, audit complet clients/projets/collections.
  Renouveler seulement UpdatedAt des clients/projets importés différents ; conserver
  dates originales dans le journal de transformations. Conserver inconnus/documents.
- [x] Ajouter surcharge Prepare(..., IReadOnlySet<Guid> inactiveMissingCollections).
  Accepter uniquement collections réellement sans dossier, copier octets/hash en
  annexe privée, retirer de la copie uniquement ; aucune exclusion par défaut.
- [x] GREEN : tests corruption/identifiants/liens/annexe/retour sans modification source.
  Rapport détaillé et revue. Ne pas éditer serveur/desktop/Remote contrats de tâche2.

Exemple de contrat de test :
```csharp
Assert.Throws<InvalidDataException>(() => service.BuildCandidate(pc, server, candidate,
    plan, Array.Empty<WorkspaceReconciliationChoice>()));
Assert.Equal(before, File.ReadAllBytes(original));
```

### Task 2: API de réception et activation récupérable

Files : Core/Remote/WorkspaceMigrationContracts.cs, Server/Migration/*,
ServerApplication.cs, ServerStorage.cs, CommandStore.cs, WorkspaceEndpoints.cs
pour partage antiforgery ; tests Server.Tests/WorkspaceMigrationApiTests.cs et recovery.

- [x] RED authentification/CSRF/HTTPS/limites, morceaux idempotents et hash global,
  chemins absents des réponses et état privé ; ancienne API non changée.
- [x] Opérations atomiques privées par GUID, taille/hash bornés ; corps brut512Kio.
  Ne pas relever globalement la limite8192. Réception jamais dans l’atelier actif.
- [x] Préparer Stage vérifié puis instantané serveur cohérent sous CommandStore.Read,
  sauvegarde complète vérifiée ; Compare et décisions versionnées. Candidate reconstruite
  après choix ; pas de candidate favorable si audits échouent.
- [x] Apply confirmé et ReviewHash exact : verrou métier, refus de Pending métier,
  revalidation serveur/candidate, journal flush, déplacement ancienroot vers backup
  conservé puis candidate vers racine stable. Après début de bascule, finir ou restaurer
  même si RequestAborted. Reçu appliqué idempotent ; autre commande/plan refusé.
- [x] Récupération avant ServerStorage.Directory.Exists au redémarrage. Chemins journal
  strictement dérivés de configuration + GUID, liens refusés, ambiguïté => failclosed.
- [x] GREEN Windows et Linux : simuler états avant/après chaque déplacement, perte réponse,
  nouvelle modification pendant revue, ancienne version d’édition refusée après reprise.
  Sauvegardes/identités/auth/commandes intactes. Rapport et revue.

### Task 3: Client natif et assistant de reprise

Files : Core/Remote/RemoteWorkspaceClient.Migration.cs (classe rendue partial),
Core/Migration/GuidedWorkspaceMigration*.cs pour préparation/journal local si utile,
Desktop/MainWindow.Migration.cs, vue dédiée native du panneau latéral,
MainWindow.Remote.cs/Settings.cs pour points d’entrée ; tests dédiés Headless et clientHTTP.

- [x] RED ancien serveur404 capabilities => message mise à jour sans transfert ;
  authentification nativeHTTPS, annulation et reprise au même compte/serveur.
- [x] Sauvegarde originale vérifiée hors source, préparation portable et bilan des
  anomalies ; choix explicite d’annexe des collections manquantes via surcharge tâche1.
  Stabilité PC avant/après export et avant demande Apply, jamais réécrire le PC.
- [x] Transfert stream/morceaux et octets réels, pas pourcentage fictif ; aucune archive
  complète en mémoire. Client dédié sans timeout20s global du tableau de bord, avec
  annulation et limites explicites. Pas de secret dans journal local ni URL.
- [x] Comparaison lisible nom/type/chemin/dates/tailles/empreintes ; choix complet PC ou
  serveur par conflit, serveur-seul conservé. Garder erreurs et choix visibles.
- [x] Confirmation finale Annuler focus/Entrée sûr ; journal local avant Apply. Réponse
  perdue => GET status uniquement, jamais nouveau Apply. Après Applied contrôlerreçu
  et source PC ; proposer mode connecté explicitement si source restée stable.
- [x] Bloquer changements de mode/opérations locales pendant instantané et activation ;
  annuler/fermer laisse sauvegardes et opération reprenable. Vérifier que fichiers réels,
  autres ateliers et compte différent ne peuvent être engagés par un ancien callback.
- [x] GREEN parcours réel sur données fictives + Headless1400/960 captures bornées,
  réutiliser styles PRODUCT/DESIGN. Rapport et revue.

### Task 4: Intégration et livraison vérifiée

- [x] Core + serveur complets, contrôles Linux, Headless, revue transversale et fixes.
- [x] Préparer image serveur candidate et guide de mise à jour : production actuellement
  épinglée à989d30f ne prend pas cette API. Ne pas annoncer déploiement/import effectués.
- [x] PublierWindows, vérifier application fermée ; backupEXE sous outputs/backups,
  remplacer unique MS3DPRINT Manager.exe et comparerSHA256. Aucune instance supplémentaire.
- [x] Docsduparcours, commit/pushbranche, rapport des preuves/limites et prochaine action
  concrète pour mettreàjourleserveur sanspriseenmainduPC.
