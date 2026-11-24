# Audit du 23 novembre 2026 — code écrit depuis le 7 novembre

**Ce que fait le dépôt :** MS3DPRINT Manager organise l'atelier (clients, projets, fichiers 3D,
devis, achats, recettes) sur PC Windows/Linux, avec une app Android et un serveur d'accès
distant (MS3DFORGE) qui synchronise fiches et tâches entre appareils.
**Charge supposée :** un seul propriétaire, 2 ou 3 appareils, un serveur accessible depuis internet.

Les audits précédents (jusqu'au 7 novembre) couvraient déjà la logique métier. Celui-ci porte sur
les 47 commits suivants : tâches, synchronisation, serveur (connexion, téléchargements), lecture
des estimations du slicer et mises à jour automatiques.

## À corriger

### 1. La mise à jour ferme le logiciel sans passer par la fermeture normale
`Windows.App/Program.cs:71-76`, `Desktop/MainWindow.Updates.cs:62`

- **Ce que c'est :** après « Mettre à jour et redémarrer », Velopack télécharge la nouvelle
  version puis appelle `ApplyUpdatesAndRestart`, qui arrête le processus immédiatement.
- **Problème :** les gestionnaires de fermeture ne s'exécutent pas. Le brouillon de devis
  n'est pas enregistré une dernière fois, la position de la fenêtre non plus. Si un import de
  fichiers a démarré pendant le téléchargement (qui peut durer une minute), la copie est
  coupée net.
- **Correction :** utiliser `WaitExitThenApplyUpdates(pending, restart: true)`, puis fermer
  l'application normalement (`_closeConfirmed = true; lifetime.Shutdown()`). La fermeture
  enregistre alors le brouillon comme d'habitude. Ne pas lancer l'installation si `_busy > 0`.
- **Si on ne fait rien :** les dernières secondes de saisie d'un devis peuvent être perdues et
  un import peut s'arrêter à moitié.

### 2. Un nom de liste de tâches avec une espace en trop bloque les listes
`Core/Tasks/TaskListStore.cs:39-44`

- **Ce que c'est :** la synchronisation fusionne les listes reçues par leur nom.
- **Problème :** la recherche utilise le nom tel qu'il est reçu (« Achats␣ »), mais
  l'enregistrement utilise le nom nettoyé (« Achats »). Le fichier contient alors deux fois
  « Achats ». Le chargement suivant (`ToDictionary`) plante sur ce doublon : chaque ajout,
  retrait ou synchronisation de liste échoue, sur le serveur comme sur le PC.
- **Correction :** nettoyer le nom avant de le chercher (`var name = entry.Name.Trim();` puis
  utiliser `name` pour la recherche et l'enregistrement), plus un test.
- **Si on ne fait rien :** une seule requête mal formée suffit à bloquer les listes jusqu'à une
  réparation manuelle du fichier `task-lists.json`.

## À améliorer

### 3. N'importe qui peut empêcher la connexion du propriétaire
`Server/ServerApplication.cs:51-56`

- **Ce que c'est :** la connexion est limitée à 20 essais par minute.
- **Problème :** la limite est commune à tout internet (une seule clé, `"owner-login"`).
  Quelqu'un qui envoie 20 faux essais par minute empêche le vrai propriétaire de se connecter.
- **Correction :** une limite par adresse IP : partitionner sur
  `context.Connection.RemoteIpAddress`. Avec un mot de passe de 16 caractères minimum,
  la limite par IP suffit contre les essais au hasard.
- **Si on ne fait rien :** une gêne facile à provoquer depuis l'extérieur, sans fuite de données.

## Bilan

Rien de grave, mais corrigez d'abord le point 1 : c'est le seul qui peut faire perdre du travail.
Le reste du code relu est soigné : mots de passe hachés, jetons aléatoires, protection CSRF,
HTTPS obligatoire, écritures de fichiers atomiques et commandes rejouables sans doublon.
Ces parties sont couvertes par des tests.

**Non vérifié :** l'app Android, l'interface web (`wwwroot/app.js`), l'aperçu PDF
(`DocumentPreviewWindow`, `PdfTextLayer`) et le détail des écrans Avalonia.
