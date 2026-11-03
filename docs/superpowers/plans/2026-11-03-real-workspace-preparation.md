# Reprise de l’atelier PC — préparation portable

Suite de l’architecture d’accès distant approuvée, autorisée dans le chat le
3 novembre 2026. Branche existante `feature/ms3dforge-remote`, sans worktree.

## État constaté

Le compagnon est accessible avec le compte propriétaire, mais son atelier est
neuf. L’atelier PC par défaut contient 14 fiches clients, 20 projets et 217
fichiers de métadonnées. Les JSON examinés sont lisibles. 52 champs racine
`RelativePath` utilisent des séparateurs Windows : clients, projets,
collections et anciennes fiches conservées dans l’historique.

Le logiciel PC est ouvert. Une copie vérifiée peut servir à la préparation ;
elle ne constitue pas une bascule finale avec écritures arrêtées.

## Préparation

- [x] Identifier la racine, inventorier les métadonnées et mesurer les documents.
- [x] Créer une archive originale par `WorkspaceBackupService`, vérifier son
  empreinte, la restaurer à part et comparer les contenus avec la source.
  Conserver cette sauvegarde hors du volume serveur et hors de Nextcloud.
- [x] Préparer une nouvelle copie à partir de cette archive : conversion
  explicitement limitée aux champs des schémas reconnus, chemins sûrs,
  identifiants et dates préservés. Refuser les cas inconnus ou ambigus.
- [x] Conserver un journal durable des transformations et leurs empreintes.
  Produire une archive portable distincte et passer les audits existants.
- [x] Tester sur données fictives les chemins Windows/Linux, la conservation
  des documents et champs inconnus, les archives altérées et les refus sûrs.
- [x] Exécuter la préparation sur la copie réelle et examiner tous les refus.

85 tests serveur réussis sous Windows et Linux. Restauration de la copie réelle
sous Linux : 345 fichiers, 14 clients et 20 projets, tous les contrôles favorables.
61 fichiers de métadonnées adaptés ; les documents sont conservés sans conversion.
Les journaux JSONL sont également contrôlés et leurs montants/historiques préservés.

L’utilisateur a autorisé la conservation séparée de deux anciennes fiches de
collection sans dossier ; leurs octets et SHA256 restent dans une annexe privée
de la sauvegarde originale. Le PC est inchangé. Les éléments annexes hors
atelier (échange mobile et PDF racine) sont également copiés et vérifiés à part.
Les archives réelles et rapports nominatifs sont privés, hors Git.

Empreinte de la copie portable vérifiée :
`0082812965AC2EDA92BC35E80D1E18F074A968824299DD6319B28DF772F98F82`.
Cette copie a été préparée pendant que Manager était ouvert, puis revérifiée
après sa fermeture : aucun fichier ajouté/retiré ni contenu modifié dans le
périmètre sauvegardé.

## Transfert et état prêt à activer

- [x] Chiffrer l’archive en CMS AES-256-GCM pour une clé privée restant sur le
  serveur, transférer deux morceaux dans le dossier privé du compagnon, vérifier
  les empreintes de chaque morceau et de leur concaténation.
- [x] Déchiffrer côté serveur et confirmer le SHA256 de l’archive portable.
- [x] Restaurer par la CLI du serveur déployé et obtenir un audit favorable.
- [x] Préparer le dossier distinct
  `/data/workspace-import-cfbc58a3123e48f989fe398896d2589f` : 14 clients, 20 projets.
- [x] Sauvegarder l’état d’authentification, les clés, les reçus et l’atelier vide
  pendant un bref arrêt du seul compagnon ; redémarrer à l’identique.
  Archive hors volume : `/var/backups/ms3dforge-before-import-cfbc58a3.tar.gz`.
  Extraction dans un dossier privé séparé et comparaison des octets réussies.
  Cette sauvegarde d’état reste sur le même hôte ; elle n’est pas une copie
  distante contre la perte de l’hôte. La sauvegarde métier originale est sur PC.
- [ ] Faire valider la bascule, configurer uniquement à l’exécution
  `Server__WorkspaceRoot=/data/workspace-import-cfbc58a3123e48f989fe398896d2589f`,
  redéployer le même commit épinglé et vérifier la session web et les comptes.

L’ancien atelier `/data/workspace` reste conservé. Retour avant toute nouvelle
écriture distante : retirer cette surcharge de configuration et redémarrer.
Après de nouvelles écritures, préserver et comparer les deux états avant un
retour ; ne pas masquer des modifications récentes par une bascule aveugle.
Le code déployé reste épinglé à `989d30f3ba11fb7b65787c569d1d894cfda1e8aa`.

## Exclusions et activation

`98_DEVELOPPEMENT` contient le dépôt et les livrables : reste local, hors atelier
serveur. L’échange mobile et le PDF situé à la racine doivent être recensés et
préservés séparément ; aucune exclusion silencieuse. La configuration locale
du PC ne doit pas être activée sur le serveur.

Le transfert doit passer par un canal privé authentifié, avec comparaison
SHA256 côté serveur. Aucun endpoint public d’import n’est ajouté pour contourner
la limite actuelle des requêtes. Ne jamais publier une archive contenant les
données utilisateur dans Git, les images Docker ou un répertoire web.

La bascule reste une étape distincte : arrêter normalement les écritures,
refaire un instantané stable, sauvegarder ensemble l’état et l’atelier serveur,
revérifier la copie puis demander la validation prévue par la spécification.
Ne pas écraser un atelier actif. Tester le retour arrière. Les deux sites
commerciaux restent hors périmètre.

La synchronisation PC et les modules absents du web restent à développer après
cette reprise ; une copie importée ne doit pas être annoncée comme synchronisée.

