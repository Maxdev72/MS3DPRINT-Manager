# Atelier unique MS3DForge et documents Nextcloud

Date : 6 novembre 2026. Statut : spécification pour implémentation, sans livraison ni activation associée.

## 1. Décision retenue

Un compte MS3DForge retrouve un atelier unique sur Windows, Ubuntu/Linux Mint, le web et Android. Les données métier sont centralisées dans MS3DForge. Les documents sont conservés dans Nextcloud et possèdent une vraie copie locale sur chaque PC.

Manager présente cet ensemble comme un seul atelier. L'utilisateur ne choisit pas une source de données à chaque lancement et ne déplace pas ses fiches pour changer de PC.

La connexion MS3DForge et l'autorisation Nextcloud sont distinctes. Elles sont configurées une première fois ; cela ne signifie pas saisir deux mots de passe à chaque ouverture.

## 2. Répartition des données

| Contenu | Référence principale | Copie sur le PC |
|---|---|---|
| Clients, projets, tâches | MS3DForge | Copie privée de consultation |
| Devis, achats, recettes, stocks, imprimantes, collections, réglages d'atelier | MS3DForge après portage de chaque module | Copie privée de consultation |
| PDF, STL, 3MF, images, factures et autres pièces | Nextcloud | Fichiers complets dans un dossier normal |
| Préférences propres au PC | Profil privé de l'application | Sur cet appareil |
| Code et compilations | Dossier de développement et Git | Sans rapport avec l'atelier |

Un PDF de facture est un document Nextcloud ; son numéro, ses montants et son statut restent dans MS3DForge. Une collection possède une fiche métier et des modèles 3D associés, selon cette même séparation.

## 3. Premier PC et reprise de l'atelier

1. Ouvrir Manager et se connecter, ou restaurer une session encore valide.
2. Retrouver l'atelier autorisé pour ce compte.
3. Pour un atelier existant, vérifier une sauvegarde avant la reprise des données.
4. Associer le dossier Nextcloud de cet atelier. Le détecter automatiquement ou demander un seul choix explicite si plusieurs dossiers sont possibles.
5. Vérifier que les documents sont réellement téléchargés sur le PC.
6. Ouvrir l'atelier et indiquer séparément l'état des données et des documents.

Un dossier vide, un disque déconnecté ou un mauvais compte ne déclenche jamais la création silencieuse d'un nouvel atelier.

## 4. Deuxième PC

Installer Manager et le client Nextcloud, puis autoriser les deux services une première fois. Manager retrouve le même atelier et reconnaît son dossier, même avec un chemin différent : par exemple `C:\Atelier\Documents` sur un PC et `D:\Nextcloud\Atelier` sur l'autre.

Aucune copie manuelle de fiches métier n'est nécessaire. Pendant le téléchargement initial, les données métier sont consultables et les documents non encore disponibles localement sont signalés. Une copie partielle n'est pas déclarée complète.

## 5. Connexion et hors connexion

- Appareil neuf : connexion requise avant d'accéder à l'atelier distant.
- Appareil connu : restauration automatique d'une session encore valide.
- Session expirée ou révoquée : nouvelle connexion pour les opérations en ligne.
- Sans réseau : consultation de la dernière copie vérifiée et de ses documents présents.
- Première version : pas d'édition métier hors connexion. Les documents restent utilisables avec les logiciels habituels et Nextcloud assure leur synchronisation au retour du réseau.
- Une panne de connexion ne bascule jamais vers un autre atelier local modifiable.
- Déconnecter Manager n'efface pas les documents du PC. Leur effacement est une action distincte et confirmée.

La connexion seule ne rend pas un module synchronisé : les modules qui écrivent encore uniquement dans l'ancien atelier local doivent être portés avant la livraison complète.

## 6. Utilisation quotidienne

**Modifier une donnée métier.** Manager l'envoie à MS3DForge. Après confirmation du serveur, elle devient la référence et les autres appareils la récupèrent à leur prochaine actualisation.

**Ajouter un document dans Manager.** Le fichier est placé dans le dossier local Nextcloud du projet. Il est immédiatement accessible sur ce PC. Nextcloud assure l'envoi ; Manager vérifie la présence distante avant d'annoncer sa disponibilité sur les autres appareils. Si Nextcloud est arrêté : « Document enregistré sur ce PC ; envoi Nextcloud en attente ».

**Modifier un document dans l'Explorateur.** Cela reste possible sans Manager. Nextcloud synchronise les fichiers ; Manager actualise ensuite ses liens et son index, sans lancer un deuxième moteur de synchronisation sur le même dossier.

**Créer un projet.** MS3DForge crée sa fiche et suit la préparation du dossier Nextcloud. En cas de délai, afficher « Dossier en cours de préparation », sans créer un deuxième projet lors d'une nouvelle tentative.

## 7. Copie locale obligatoire

Tous les documents compris dans le périmètre atelier doivent être conservés intégralement sur chaque PC déclaré prêt. Une icône de fichier à télécharger ne suffit pas.

Configurer un miroir complet du périmètre ou son maintien permanent hors connexion avec les fichiers virtuels. Vérifier la lisibilité des fichiers et l'espace disponible sur les versions Nextcloud retenues pour Windows et Linux. Aucun effacement automatique pour faire de la place.

Les miniatures, caches de rendu, compilations et journaux techniques restent hors du dossier documentaire synchronisé. Les documents restent ouverts avec les logiciels habituels lorsque Manager est fermé.

## 8. Téléphone et web

Le même compte MS3DForge retrouve les fiches et pièces jointes sur téléphone, sans laisser un PC allumé. Le serveur fournit un accès autorisé aux documents Nextcloud.

Les serveurs MS3DForge et Nextcloud doivent rester disponibles et pouvoir communiquer. Si Nextcloud n'est accessible que sur le réseau de la maison, prévoir un accès réseau autorisé adapté et le tester depuis l'extérieur avant de promettre cet usage.

Sur Android, l'utilisateur conserve des documents ou projets sélectionnés hors connexion dans un stockage persistant et vérifié. Pas de téléchargement intégral automatique de tous les modèles 3D. Une application associée peut ouvrir les fichiers conservés.

L'app Nextcloud reste utilisable indépendamment. Sa copie et celle de Manager ne sont pas supposées identiques sans vérification : le parcours de référence garantit d'abord les fichiers explicitement conservés par Manager. La navigation web permet la consultation et le téléchargement, sans promettre un miroir permanent de dossiers sur téléphone.

Android peut différer les transferts en arrière-plan. Actualiser aussi à l'ouverture et à la reprise ; ne pas promettre un transfert instantané lorsque le système suspend l'application.

## 9. États à afficher

| État | Signification |
|---|---|
| Données à jour | Lectures et commandes connues confirmées par MS3DForge |
| Documents présents sur ce PC | Copie locale complète du périmètre vérifié |
| Documents en cours d'envoi | Une version locale attend encore une confirmation Nextcloud |
| Vérification des documents | État distant ou index pas encore confirmé |
| Hors connexion | Dernière copie disponible, avec date de vérification |
| À vérifier | Conflit, permission ou dossier manquant nécessitant une action |

Un badge global « Tout est à jour » exige la confirmation des deux circuits. L'enregistrement d'une facture métier ne prouve pas que son nouveau PDF a déjà quitté le PC.

## 10. Conflits et suppressions

Comparer les versions de départ des modifications métier concurrentes ; ne pas écraser une version silencieusement et ne pas choisir selon l'horloge des PC.

Conserver et signaler les fichiers en conflit produits par Nextcloud. Suivre l'identité d'un document renommé quand elle est disponible. Une disparition de fichier ne supprime jamais automatiquement la fiche métier associée.

Depuis Manager, confirmer les suppressions et utiliser la récupération prévue. Une suppression directe dans l'Explorateur suit le comportement de Nextcloud : Manager ne peut pas imposer une confirmation en dehors de son interface.

La synchronisation répercute aussi des suppressions et erreurs ; conserver une sauvegarde indépendante.

## 11. Migration

Inventorier séparément fiches, documents et atelier serveur. Ne pas fusionner automatiquement une ancienne copie de développement/démonstration. Vérifier les sauvegardes et conserver les originaux. Reprendre les données PC choisies lorsqu'elles sont plus récentes et réutiliser les documents Nextcloud déjà corrects.

Conserver les identifiants clients/projets et associer leurs documents. Présenter un bilan des quantités, conflits, éléments conservés à part et fichiers introuvables. Exiger une confirmation finale puis conserver un reçu.

Vérifier le deuxième PC et le téléphone avant de terminer la bascule. Les anciennes fiches restent une sauvegarde consultable, sans devenir une deuxième base modifiable. Fermer ou mettre à jour les anciens logiciels avant la reprise de l'activité.

## 12. État actuel et lots à livrer

Le dépôt possède déjà : connexion native, coffres Windows/Linux, API clients/projets/tâches, reçus de commandes, copie de consultation, migration vérifiée, détection partielle des dossiers cloud, hôtes Windows/Linux/Android et site compagnon.

| Lot | Résultat attendu |
|---|---|
| 1 | Compte, atelier et préférences retrouvés indépendamment du dossier du programme |
| 2 | Dossier Nextcloud reconnu et copie locale contrôlée sur deux PC |
| 3 | Documents reliés aux projets, y compris après ajout depuis l'Explorateur |
| 4 | Tous les modules métier raccordés au serveur |
| 5 | Documents web/Android et sélection hors connexion sur téléphone |
| 6 | Migration réelle vérifiée et bascule générale |

Google Drive, Dropbox et un second stockage documentaire synchronisé ne font pas partie de cette première intégration.

Le travail est terminé lorsque les modifications de PC A sont retrouvées sur PC B et téléphone, les documents du PC s'ouvrent sans Internet ni Manager, et les éléments non transmis sont identifiés clairement. Un écran de connexion seul ne satisfait pas ce critère.

Détails : [conception technique](superpowers/specs/2026-11-06-atelier-unifie-nextcloud-design.md) et [plan d'implémentation](superpowers/plans/2026-11-06-atelier-unifie-nextcloud.md).
