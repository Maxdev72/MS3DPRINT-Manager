# Synchronisation automatique PC / serveur — plan

> Exécution dans cette session avec executing-plans, branche dédiée existante.

**Objectif :** Actualiser automatiquement les clients, projets et tâches déjà
exposés par l’API, avec copie de consultation persistante sur PC. Le serveur
reste la référence. Cette tranche poursuit la conception approuvée
`docs/superpowers/specs/2026-11-02-acces-distant-design.md`.

**Architecture :** Réutiliser RemoteWorkspaceClient et la vue native existante.
La copie PC est un JSON atomique sous LocalApplicationData, distinct de l’atelier
Nextcloud ; elle contient serveur, compte, date, listes et commande incertaine,
jamais mot de passe ni jeton. Le timer ne transmet que des lectures et ne rejoue
aucune commande. Les écritures restent des commandes HTTPS versionnées.

**Contraintes :** Aucune prise de contrôle, aucun déploiement, aucune modification
des données réelles ou des deux sites commerciaux. Pas de fusion de l’atelier
local historique. Une seule livraison Windows « MS3DPRINT Manager.exe ».
Aucune nouvelle dépendance. Les autres modules et fichiers restent hors API.

## 1. Copie persistante séparée

Fichiers : Core/Remote/RemoteWorkspaceCache.cs, tests/RemoteWorkspaceCacheTests.cs.
Interface : RemoteWorkspaceCache(path).Load(), Save(RemoteWorkspaceCopy), Clear().
RemoteWorkspaceCopy contient Server, Account, ReadAt, Clients, Projects et PendingCommand.

- [x] Tester aller-retour, absence de secrets, refus de données invalides et
  conservation de la copie précédente après une sauvegarde refusée.
- [x] Constater les échecs ; implémenter validation HTTPS/compte, identifiants et
  liens clients-projets, JSON strict, écriture temporaire puis remplacement,
  refus des liens filesystem. Une copie illisible est signalée, jamais vide.
- [x] Vérifier que le constructeur n’écrit pas dans l’atelier et que les tests passent.

## 2. Synchronisation dans la vue native

Fichiers : Desktop/Remote/RemoteWorkspaceView.cs et .Synchronization.cs,
Desktop/MainWindow.Settings.cs, Android.csproj (lien du partial partagé),
tests/RemoteWorkspaceUiTests.cs.
Interface : SynchronizeServerAsync() appelée par le timer (30 secondes),
RefreshServerAsync() reste l’actualisation manuelle.

- [x] Tester lecture automatique, panne/reprise, brouillon et version conservés,
  absence de rejeu et arrêt après Dispose. Tester réouverture depuis copie sans
  session et commande incertaine persistante.
- [x] Implémenter timer sans requêtes chevauchées, dernière copie lisible hors
  connexion, login explicite avec compte/serveur visibles. Expiration de session
  bloque les écritures et demande la reconnexion ; aucun mot de passe enregistré.
- [x] Journaliser l’identifiant avant un envoi ; si l’écriture du journal échoue,
  ne pas envoyer. Après interruption, garder le blocage et consulter uniquement
  le reçu. Rafraîchir ne lève jamais l’incertitude.
- [x] Ne pas rebaser automatiquement un brouillon sur une nouvelle version du
  projet : un changement concurrent doit encore produire un conflit.
- [x] Relier le cache seulement au PC ; garder les constructions Android et
  les tests existants sans cache compatibles. Adapter le texte des paramètres.

## 3. Vérification et livraison locale

- [x] Tests Core complets, tests serveur (API inchangée), build Android du fichier
  partagé ; binlogs pour toutes les commandes .NET.
- [x] Revue locale du diff : aucune donnée réelle dans Git, jetons mémoire seule,
  brouillons et reçus préservés, pas d’écriture dans l’atelier historique.
- [ ] Publier Windows avec le profil existant, vérifier application fermée,
  sauvegarder l’ancien binaire sous outputs/backups puis remplacer l’unique EXE.
- [ ] Mettre à jour le guide et les limites, commit et push sur la branche.

## Revue et preuves

Revue indépendante : gestion explicite des copies invalides, verrou exclusif
entre instances, protection contre la déconnexion tardive d’une fenêtre fermée
et conservation de version d’un éditeur de tâche temporairement vide corrigées
avec tests reproduisant les défauts avant correction. Aucun P1/P2 restant dans
la seconde relecture. Les tests ciblés passent (35 hors minuteur), le minuteur
réel a été observé sans appel manuel après 30 secondes.

La compilation C# Android du composant partagé réussit (cible Compile). La
construction de l’APK complet échoue dans Aapt2 sur un fichier de ressources
OCR au chemin long ; aucun nouvel APK n’est annoncé livré. Cette tranche
concerne le PC, l’APK précédent reste conservé.

Suite finale après revue : 1 245 tests Core réussis en Release, 0 échec
(`outputs/sync-verification/sync-core-reviewed.trx`). Serveur : 85 réussis,
0 échec (`sync-server-final.trx`). Publication Windows autonome réussie.
