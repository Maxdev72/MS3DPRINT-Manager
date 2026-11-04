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

### Task 1: Connexion commune et coffre Windows

Fichiers Core/Remote/RemoteDeviceSession.cs, RemoteWorkspaceConnection.cs,
RemoteWorkspaceClient.cs ; Windows/Remote/WindowsRemoteSessionStore.cs ; tests dédiés.

Interfaces publiques à produire :
`RemoteDeviceSession` classe avec Server, Account, AccessToken, ExpiresAt, ToString sans secret.
`IRemoteSessionStore { RemoteDeviceSession? Load(); void Save(RemoteDeviceSession session); void Clear(); }`.
`RemoteWorkspaceConnection(HttpClient http, RemoteWorkspaceCache cache, IRemoteSessionStore? sessions = null)` IDisposable.
Propriétés Copy (RemoteWorkspaceCopy?), IsAuthenticated, IsOnline, CanWrite, Account, Server.
Méthodes RestoreAsync(), LoginAsync(Uri server, string account, string password, bool remember),
RefreshAsync(), LogoutAsync(), CheckReceiptAsync(), ReadAsync<T>(Func<RemoteWorkspaceClient,Task<T>> read), ExecuteAsync<T>(Guid id, Func<RemoteWorkspaceClient,Task<T>> send).
Toutes les opérations acceptent CancellationToken optionnel hors ExecuteAsync qui
utilise le token du contrôleur. Ne jamais enregistrer le mot de passe.
Copy contient toujours PendingCommand et le journal survit au Dispose en cours d’envoi.
Un contrôleur garde son verrou jusqu’à Dispose ; après Dispose aucune écriture disque.
WindowsRemoteSessionStore implémente IRemoteSessionStore avec Credential Manager.

- [x] Tests rouges puis contrôleur et persistance sécurisée ; couvre révocation,
  expiration, mauvaise destination, panne, conflits, reçu, concurrence, fermeture.
- [x] Vérifier séparément Core/Windows ciblés et produire rapport/revue.

### Task 2: Pages principales et connexion au démarrage

Fichiers MainWindow.Remote*.cs ; accroches dans App, MainWindow startup,
navigation, présentation, listes/création clients/projets, paramètres et boutons
projet/tâches. Hôte Windows configure une factory IRemoteSessionStore sur
DesktopApplication ; Desktop ne référence pas Windows.

- [x] Tests Headless rouges : ouverture directe en mode serveur, restauration,
  menus Clients/Projets sur API, actions métier envoyées au serveur seulement,
  modules non connectés bloqués, bascule locale explicite et panne sans bascule.
- [x] Intégrer connexion et bandeau dans le shell habituel ; réutiliser styles,
  lignes, dialogues et pagination. Aucune fenêtre secondaire d’atelier.
- [x] Brancher les commandes supportées, contrôles de version et reçus ; timer
  30 secondes sans réinitialiser formulaire, sélection, tri ou recherche.
- [x] Garde de mode à toutes les entrées locales accessibles, y compris menus,
  raccourcis, callbacks et publication mobile existante. Contrôle explicite
  d’absence d’écriture locale dans les tests. Lire la copie hors connexion.
- [x] Revue du lot interface et intégration des deux tâches.

### Task 3: Vérification et livraison

- [x] Suite Core complète + serveur + vérification coffre Windows et parcours
  Headless ; preuves stockées sous outputs/integrated-server-workspace.
- [x] Revue globale, corriger puis vérifier uniquement les modifications finales.
- [ ] Publier Windows, attendre fermeture normale si nécessaire, sauvegarder puis
  remplacer l’EXE unique et comparer SHA256 ; aucun lancement supplémentaire.
- [ ] Guide, limites, commit et push de la branche, sans déployer la production.

## État de validation — 4 novembre 2026

Task 1 acceptée après revue indépendante : 45 tests ciblés Core/Windows réussis,
0 avertissement et 0 erreur ; lecture/écriture/suppression d’une entrée de test
unique dans le coffre Windows vérifiées. Aucun identifiant réel accédé.
Commit `90958fa`. Les réponses tardives après remplacement du contrôleur et
la révocation pendant une vérification de reçu sont couvertes.

Suite serveur exécutée le 4 novembre : 85 réussis, 0 échec, API inchangée.
Le lot interface et ses parcours complets restent à vérifier avant livraison.

Task 2 acceptée après corrections de revue : tests ciblés natifs et parcours
de réouverture de devis local réussis. La première suite générale a identifié
une perte de hauteur de l’estimateur local ; le bandeau serveur est désormais
masqué en mode local et l’accès serveur reste dans la barre latérale. La suite
générale suivante a réussi 1 286 tests avant l’amendement de priorité PC.

L’utilisateur précise ensuite que ses données PC sont plus récentes. Le
démarrage sans préférence sur un atelier existant reste donc local ; les
dossiers métier sont détectés sans exploration récursive et une indisponibilité
d’un atelier enregistré ne devient pas une installation neuve. Le passage
explicite au serveur avertit qu’aucun transfert PC n’est effectué. Les écritures
actives sont revérifiées après cette confirmation. Les 11 tests ciblés finaux
réussissent ; la revue de source ne relève plus de P1/P2. Une dernière suite
générale est exécutée sur cet amendement avant la livraison.

Validation finale sur le code figé avec la priorité PC : **1 288 tests Core
réussis, 0 échec**, suite Release complète ; **85 tests serveur réussis**.
Le coffre Windows et contrôleur sont également validés par 45 tests ciblés.
La revue finale ne relève plus de P1/P2. Rapport TRX local :
`outputs/integrated-server-workspace/core-suite-pc-priority.trx`.

L’atelier PC reste la copie la plus récente déclarée par l’utilisateur.
Aucune migration de ses nouvelles données ni aucune modification de production
n’est incluse dans cette livraison du parcours de connexion intégré.
