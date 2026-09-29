# Stockages et performances — conception

## Objectif

Préparer MS3DPRINT Manager à utiliser plusieurs stockages synchronisés et afficher les capacités matérielles du poste, sans modifier le dossier Nextcloud actuellement utilisé ni prétendre accélérer les opérations de fichiers avec le GPU.

## Périmètre de cette version

La fenêtre **Paramètres** devient défilable et regroupe trois sections :

1. **Apparence** conserve le choix Clair, Sombre ou Automatique.
2. **Stockages** affiche des cartes de fournisseurs :
   - Nextcloud : actif, avec le chemin actuellement configuré ;
   - Google Drive : logo officiel et état « Bientôt disponible » ;
   - Dropbox : logo officiel et état « Bientôt disponible » ;
   - Dossier local : icône neutre et état « Bientôt disponible ».
3. **Performances** affiche les informations de diagnostic suivantes : nom du processeur, nombre de processeurs logiques (threads), mémoire physique totale et une ligne par carte graphique détectée.

Une information précise que l'accélération GPU sera réservée à de futurs traitements lourds (aperçus 3D, conversion ou rendu) et qu'aucun traitement actuel n'est accéléré artificiellement.

## Architecture

### Stockages

Un modèle interne `StorageProviderDefinition` décrit un fournisseur par son identifiant, son nom affiché, son état et son chemin facultatif. Les cartes de la fenêtre Paramètres sont construites à partir de ces définitions.

Dans cette version, seul Nextcloud reçoit un chemin et reste le stockage actif. Les trois autres fournisseurs sont des définitions non sélectionnables. Cette limite préserve à l'identique toutes les créations, vérifications et tous les classements de fichiers existants.

Une évolution ultérieure pourra ajouter un `StorageSettingsStore` et permettre de sélectionner un dossier local synchronisé par fournisseur, sans nécessiter d'API cloud ni de jeton d'authentification.

### Diagnostic matériel

`SystemHardwareInfoService` fournit un instantané immuable des capacités du poste. La détection emploie `Environment.ProcessorCount` pour le nombre de threads et les informations système Windows pour le nom du processeur, la mémoire et les contrôleurs vidéo. Chaque information absente reçoit une valeur de repli lisible : `Non détecté`.

La lecture est effectuée une seule fois lors de l'ouverture des Paramètres, hors du fil d'interface si une requête Windows est nécessaire. Il n'y a ni sondage en continu ni benchmark.

### Préférence de performance

Une valeur `Automatic` est exposée uniquement comme préparation. Les futurs traitements pourront choisir un niveau de parallélisme à partir du nombre de threads détectés et vérifier la disponibilité d'un GPU compatible. Aucune routine actuelle de fichiers ne reçoit de parallélisme supplémentaire.

## Interface et accessibilité

Les cartes de stockage ont un état explicite, non interactif lorsqu'un fournisseur est indisponible, avec un contraste correct dans les thèmes clair et sombre. Les logos fournisseurs proviennent de leurs actifs officiels et seront regroupés dans les ressources locales de l'application ; la diffusion publique devra respecter les règles de marque de chaque fournisseur.

Les données de diagnostic ne contiennent aucun identifiant matériel, numéro de série, adresse réseau ou donnée transmise à un service externe.

## Erreurs et performances

Une erreur de détection d'un composant ne ferme jamais les Paramètres. La ligne concernée affiche `Non détecté`. La fenêtre s'ouvre sans attendre un service cloud et la détection est mise en cache pour la durée de la fenêtre.

## Tests

- tests unitaires du modèle de fournisseur et du service matériel avec valeurs de repli ;
- tests de marquage XAML pour les sections Stockages et Performances ;
- tests d'absence de modification du chemin Nextcloud actif ;
- construction WPF et suite complète de régression.
