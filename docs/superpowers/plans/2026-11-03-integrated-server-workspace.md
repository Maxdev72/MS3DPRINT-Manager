# Atelier serveur intégré — plan d’implémentation

**Objectif :** Exécuter la conception validée
`docs/superpowers/specs/2026-11-03-integrated-server-workspace-design.md`.
Branche feature/ms3dforge-remote dans le dossier actuel, selon choix utilisateur.
Exécution avec subagent-driven-development ; interfaces fixées ci-dessous.

## Contraintes globales

- Aucun contrôle du PC ou navigateur, aucune modification de Coolify/production.
- Serveur référence ; aucune lecture/écriture métier locale en mode serveur.
- Pas de mot de passe sauvegardé ; jeton seulement dans le coffre Windows.
- Journal durable avant tout envoi ; aucun rejeu incertain.
- Un seul exécutable livré : MS3DPRINT Manager.exe ; précédent sous outputs/backups.
- result.json préexistant hors périmètre. Pas de nouvelle dépendance.
- Tests .NET avec binlogs ; ne pas lancer deux builds d’un même répertoire de sortie.

## 1. Connexion commune et coffre Windows

Fichiers Core/Remote/RemoteDeviceSession.cs, RemoteWorkspaceConnection.cs,
RemoteWorkspaceClient.cs ; Windows/Remote/WindowsRemoteSessionStore.cs ; tests dédiés.

Interfaces publiques à produire :
`RemoteDeviceSession` classe avec Server, Account, AccessToken, ExpiresAt, ToString sans secret.
`IRemoteSessionStore { RemoteDeviceSession? Load(); void Save(RemoteDeviceSession session); void Clear(); }`.
`RemoteWorkspaceConnection(HttpClient http, RemoteWorkspaceCache cache, IRemoteSessionStore? sessions = null)` IDisposable.
Propriétés Copy (RemoteWorkspaceCopy?), IsAuthenticated, IsOnline, CanWrite, Account, Server.
Méthodes RestoreAsync(), LoginAsync(Uri server, string account, string password, bool remember),
RefreshAsync(), LogoutAsync(), CheckReceiptAsync(), ExecuteAsync<T>(Guid id, Func<RemoteWorkspaceClient,Task<T>> send).
Toutes les opérations acceptent CancellationToken optionnel hors ExecuteAsync qui
utilise le token du contrôleur. Ne jamais enregistrer le mot de passe.
Copy contient toujours PendingCommand et le journal survit au Dispose en cours d’envoi.
Un contrôleur garde son verrou jusqu’à Dispose ; après Dispose aucune écriture disque.
WindowsRemoteSessionStore implémente IRemoteSessionStore avec Credential Manager.

- [ ] Tests rouges puis contrôleur et persistance sécurisée ; couvre révocation,
  expiration, mauvaise destination, panne, conflits, reçu, concurrence, fermeture.
- [ ] Vérifier séparément Core/Windows ciblés et produire rapport/revue.

## 2. Pages principales et connexion au démarrage

Fichiers MainWindow.Remote*.cs ; accroches dans App, MainWindow startup,
navigation, présentation, listes/création clients/projets, paramètres et boutons
projet/tâches. Hôte Windows configure une factory IRemoteSessionStore sur
DesktopApplication ; Desktop ne référence pas Windows.

- [ ] Tests Headless rouges : ouverture directe en mode serveur, restauration,
  menus Clients/Projets sur API, actions métier envoyées au serveur seulement,
  modules non connectés bloqués, bascule locale explicite et panne sans bascule.
- [ ] Intégrer connexion et bandeau dans le shell habituel ; réutiliser styles,
  lignes, dialogues et pagination. Aucune fenêtre secondaire d’atelier.
- [ ] Brancher les commandes supportées, contrôles de version et reçus ; timer
  30 secondes sans réinitialiser formulaire, sélection, tri ou recherche.
- [ ] Garde de mode à toutes les entrées locales accessibles, y compris menus,
  raccourcis, callbacks et publication mobile existante. Contrôle explicite
  d’absence d’écriture locale dans les tests. Lire la copie hors connexion.
- [ ] Revue du lot interface et intégration des deux tâches.

## 3. Vérification et livraison

- [ ] Suite Core complète + serveur + vérification coffre Windows et parcours
  Headless ; preuves stockées sous outputs/integrated-server-workspace.
- [ ] Revue globale, corriger puis vérifier uniquement les modifications finales.
- [ ] Publier Windows, attendre fermeture normale si nécessaire, sauvegarder puis
  remplacer l’EXE unique et comparer SHA256 ; aucun lancement supplémentaire.
- [ ] Guide, limites, commit et push de la branche, sans déployer la production.
