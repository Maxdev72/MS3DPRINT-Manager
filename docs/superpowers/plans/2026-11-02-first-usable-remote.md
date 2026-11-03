# Première version distante utilisable — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Continue through the tasks without asking whether to continue. A dedicated branch in the current directory is explicitly preferred by the user.

**Goal:** Livrer une première version utilisable dans la nuit du 2 au 3 novembre
2026 : connexion privée, clients, projets et tâches accessibles via le serveur,
avec une interface web adaptée au téléphone et un accès depuis les applications.

**Architecture:** L'API .NET 10 reste l'autorité ; Core porte les règles métier.
Un petit site sans framework JavaScript consomme cette même API. Le client .NET
commun pourra être utilisé par les interfaces Avalonia PC et Android.

**Spec:** `docs/superpowers/specs/2026-11-02-acces-distant-design.md`.

## Global Constraints

- Branche `feature/ms3dforge-remote`, dossier actuel, Ponytail actif.
- Pas de migration réelle ou d'activation automatique ; atelier de test neuf
  jusqu'à validation du parcours. Projets Coolify commerciaux exclus.
- HTTPS, authentification, CSRF web, sessions révocables, aucune inscription publique.
- Modifications en ligne uniquement ; version obligatoire et conflit explicite.
- Aucun écrasement silencieux ni rejet effaçant le brouillon de l'utilisateur.
- Aucun secret commité ou renvoyé dans les logs. Aucune nouvelle dépendance frontend.
- Le nom de livraison Windows reste exactement `MS3DPRINT Manager.exe`.
- La première version ne signifie pas la parité de tous les modules de Manager.

### Task 1: Commandes métier et reçus durables

Implémenter l'API d'écriture et les contrats communs. Fichiers possédés par
cette tâche : `src/MS3DPRINT.Manager.Core/Remote/RemoteContracts.cs`,
`src/MS3DPRINT.Manager.Server/Workspace/WorkspaceCommands.cs`,
`CommandStore.cs`, `WorkspaceEndpoints.cs`, et
`tests/MS3DPRINT.Manager.Server.Tests/WorkspaceCommandTests.cs`.
Modifier `ServerApplication.cs` et `Contracts.cs` seulement pour l'intégration.
Ne pas toucher aux fichiers de migration ni à `wwwroot`.

**Contrats précis :** namespace `MS3DPRINT.Manager.Core.Remote`. Records sealed,
JSON camelCase, enums sous forme de chaînes. Conserver la forme des listes
clients/projets existantes. Les types détaillés exposent les champs des fiches
Core sans RelativePath ni chemin absolu. Contact utilise `PrimaryContact`.

```csharp
public sealed record CreateClientCommand(Guid CommandId, ClientKind Kind, string ClientCode,
    string? CompanyName, string? FirstName, string? LastName, string? Address,
    string? Notes, PrimaryContact Contact);
public sealed record UpdateClientCommand(Guid CommandId, DateTimeOffset ExpectedUpdatedAt,
    ClientKind Kind, string? CompanyName, string? FirstName, string? LastName,
    string? Address, string? Notes, PrimaryContact Contact);
public sealed record CreateProjectCommand(Guid CommandId, Guid ClientId, string ProjectName,
    int Year, DateOnly? DueDate, string? Description, ProjectPriority Priority);
public sealed record UpdateProjectCommand(Guid CommandId, DateTimeOffset ExpectedUpdatedAt,
    ProjectStatus Status, DateOnly? DueDate, string? Description, string? Notes, ProjectPriority Priority);
public sealed record AddTaskCommand(Guid CommandId, DateTimeOffset ExpectedUpdatedAt, string Title, string? Notes);
public sealed record EditTaskCommand(Guid CommandId, DateTimeOffset ExpectedUpdatedAt, string Title, string? Notes);
public sealed record CompleteTaskCommand(Guid CommandId, DateTimeOffset ExpectedUpdatedAt, bool Completed);
public sealed record MoveTaskCommand(Guid CommandId, DateTimeOffset ExpectedUpdatedAt, int Direction);
public sealed record RemoveTaskCommand(Guid CommandId, DateTimeOffset ExpectedUpdatedAt, bool Confirmed);
```

Routes authentifiées :

- `GET /api/v1/clients/{id}` et `GET /api/v1/projects/{id}` : détail sans chemin.
- `POST /api/v1/clients` : CreateClientCommand ; 201 et détail client.
- `PUT /api/v1/clients/{id}` : UpdateClientCommand ; 200 et détail client.
  Le code et le dossier restent inchangés dans cette tranche.
- `POST /api/v1/projects` : CreateProjectCommand ; 201 et détail projet.
- `PUT /api/v1/projects/{id}` : UpdateProjectCommand ; 200 et détail projet.
  Nom, référence et dossier ne sont pas renommés par cette commande.
- `POST /api/v1/projects/{id}/tasks` : AddTaskCommand ; 200 et détail projet.
- `PUT /api/v1/projects/{id}/tasks/{taskId}` : EditTaskCommand.
- `POST /api/v1/projects/{id}/tasks/{taskId}/complete` : CompleteTaskCommand.
- `POST /api/v1/projects/{id}/tasks/{taskId}/move` : MoveTaskCommand (-1 ou 1).
- `POST /api/v1/projects/{id}/tasks/{taskId}/remove` : RemoveTaskCommand,
  refuse si Confirmed=false. Toutes ces routes renvoient le détail projet actualisé.
- `GET /api/v1/commands/{commandId}` : état du reçu (Completed ou Uncertain),
  code HTTP original et résultat si terminé ; 404 si inconnu.

**Règles :** validation des identifiants non vides ; nom/code/contact bornés ;
utiliser NameNormalizer et les services Core pour les créations de dossiers,
références et fiches (`ClientCreationService`, `ProjectCreationService`,
`ProjectTasks`). Vérifier les chemins de profil via WorkspaceEntityPaths et
WorkspacePathSafety, et la portabilité des nouveaux noms, même sous Linux.
Préserver les versions UpdatedAt ; un attendu périmé renvoie 409. Le PUT
client/projet ne passe pas de version nulle aux stores. Un champ requis invalide
renvoie 400 et une ressource absente 404. Une erreur interne ne révèle pas les chemins.

**Reçus :** SQLite côté StateRoot, clé CommandId unique, empreinte de la méthode,
route et JSON typé. Sous un verrou unique d'instance, écrire Pending durablement
AVANT l'opération Core, puis stocker code/contenu du résultat. Même commande
terminée : rejouer la réponse sans réexécuter. Même identifiant avec autre entrée :
409. Pending après coupure : état Uncertain et 409, aucun rejeu automatique.
Ce mécanisme ne prétend pas à une transaction atomique SQLite + fichiers.

**Sécurité :** valider l'antiforgery avant toute commande web authentifiée par
cookie ; les commandes Bearer n'utilisent pas les cookies. Ne pas exposer de
commande recevant un chemin disque arbitraire. Les lecteurs doivent partager
le verrou avec les mutations pour ne pas lire une opération multi-fichiers en cours.

- [x] Écrire les tests d'intégration puis constater l'échec : création client
  et projet, référence attribuée par Core, ajout/validation d'une tâche, conflit
  de version entre deux clients, CSRF manquant, retrait sans confirmation.
- [x] Implémenter le minimum puis vérifier ces parcours.
- [x] Tester le reçu durable : replay exact sans doublon, autre contenu en 409,
  Pending traité Uncertain sans action. Tests HTTP, pas de mocks de Core.
- [x] Exécuter la suite serveur avec binlog, auto-revue, commit uniquement les
  fichiers de la tâche. Rapport dans le workspace SDD de ce plan.

### Task 2: Interface web utilisable

Fichiers : `Server/wwwroot/index.html`, `app.css`, `app.js`. Le contrôleur
intègre les fichiers statiques et la CSP dans ServerApplication après Task 1.
Hériter du système bleu-nuit/bleu/cyan décrit par DESIGN.md, français, listes
denses et formulaires lisibles. Aucune dépendance, données inventées ou métrique
décorative. Icônes SVG fonctionnelles, responsive téléphone/PC, clavier et focus.

- [x] Écran de connexion privé, obtention de CSRF et gestion de session expirée.
- [x] Navigation clients/projets, recherche, détails, création client/projet,
  modification des champs permis et tâches (ajouter, éditer, terminer, réordonner,
  retirer après confirmation avec Annuler privilégié).
- [x] Réutiliser le même CommandId seulement pour vérifier un envoi incertain ;
  ne pas réessayer automatiquement une mutation après coupure. Conserver les
  champs en cas de conflit/erreur et afficher la dernière actualisation.
- [x] Faire un parcours réel dans le navigateur, capturer PC et téléphone,
  corriger les défauts observés et confirmer une fois.

### Task 3: Client .NET commun et interfaces PC/Android

Fichiers : `Core/Remote/RemoteWorkspaceClient.cs`, puis des composants Avalonia
dédiés et réglages locaux aux appareils. Ne pas stocker les jetons dans l'atelier
Nextcloud ; utiliser la protection du système ou une session en mémoire dans
la première version si le stockage sécurisé ne peut pas être vérifié à temps.

- [x] Client HttpClient injectable, HTTPS seulement, session Bearer, pas de retry
  de mutation, gestion explicite de 401/409/Uncertain ; mêmes contrats que Task 1.
- [x] Écran distant PC et Android : connexion, clients/projets/tâches et actions
  du périmètre. Écriture locale de l'atelier existant inchangée ; aucune fusion
  implicite entre mode local et mode distant. Coupure : lecture de la dernière
  vue en mémoire et mutations bloquées, état visible.
- [x] Tests de transport et parcours des interfaces sur données fictives ;
  compiler PC/APK. Préparer les fichiers de livraison avec nom Windows prescrit.

### Task 4: Livraison candidate et hébergement

- [x] Tests serveur sous Windows/Linux et régression adaptée aux écrans touchés.
- [ ] Construire l'image, vérifier la confiance explicite au proxy Coolify, préparer
  le service privé et son volume neuf. Aucun changement des projets commerciaux.
- [x] Mettre à disposition une candidate documentée, avec limites et tests réels.
  Toute étape nécessitant authentification utilisateur, fermeture normale de
  l'exécutable verrouillé ou validation de publication sera traitée au dernier
  moment, lorsque le résultat est concret et vérifiable.
