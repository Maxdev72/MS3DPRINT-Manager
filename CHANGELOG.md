# Journal des modifications

## 12 octobre 2026

### Passe Impeccable
- Vraies icônes dessinées (menus d’actions, boutons, tri des colonnes, notifications) à la place des symboles texte.
- Menu actif sans liseré coloré ; pages posées directement sur le fond, sans cartes imbriquées ni titres répétés.
- Accueil : trois indicateurs réunis dans un seul bandeau ; plus de défilement qui masque les boutons des listes.

### Passe de finition
- Fiche client, explorateur de dossiers et corbeille repris dans le même style de tableau (icônes, badges, dates).
- Une erreur inattendue ne ferme plus le logiciel : elle s’affiche en bas à droite et est consignée dans
  `%LOCALAPPDATA%\MS3DPRINT Manager\erreurs.log`.
- Textes complets en infobulle quand une cellule est tronquée, noms accessibles pour les boutons à icône.
- README réécrit pour décrire le logiciel actuel (navigation, clients, projets, estimations, aperçus, dépannage).

### Deuxième passe sur l’interface
- Fil d’Ariane cliquable (Projets › Projet › sous-dossier, ou Clients › Client › Projet) pour remonter en un clic.
- Colonnes triables en cliquant sur l’en-tête (Projets et Clients), clic droit sur une ligne pour ouvrir ses actions.
- Accueil : bloc « À surveiller » (échéances proches ou dépassées, fiches projet et client à compléter) avec accès direct aux listes filtrées.
- Menu : compteurs sur Projets et Clients ; en dessous de 1100 px il se réduit à une barre d’icônes.
- Recherche : résultats avec icône et type, navigation au clavier (↑ ↓ puis Entrée).
- Le logiciel retient la taille de sa fenêtre ; transitions douces sur les boutons et les pages.
- Fiche projet épurée : plus de titre en double, statut et échéance sur une ligne.

### Refonte de l’interface
- Nouveau système de design : couleurs plus contrastées, typographie, boutons (principal, secondaire, discret, danger),
  champs, badges de statut colorés et cartes cohérents dans tout le logiciel.
- Menu latéral avec icônes, repère de la page active et raccourcis clavier : Ctrl+1 à Ctrl+9 pour changer de page,
  Ctrl+N pour créer un projet, Ctrl+K pour rechercher.
- Tableau de bord : indicateurs avec icônes, cinq projets récents (statut en badge), actions rapides
  (nouveau projet, client, estimation), mis à jour à chaque retour sur l’accueil.
- Filtres de recherche et de statut dans Projets et Clients, avec le nombre de résultats.
- Fenêtres de saisie unifiées : titre, aide, champs étiquetés, boutons en pied de fenêtre ; Entrée valide, Échap annule.
- Notifications discrètes en bas à droite à la place des messages sous le titre, et barre de chargement pendant les lectures.
- États vides explicatifs avec un bouton d’action, fiche projet en tableau (icônes, type, taille), page Paramètres en cartes.

### Nouveautés
- Écran de chargement au démarrage, avec le logo, le nom du logiciel et une barre de progression.
- Les projets récents du tableau de bord sont cliquables et ouvrent directement le projet.
- Nouvelle barre de recherche (Ctrl+K), plus lisible.
- Collections : filtre par type (Modèles 3D, Produits, Fournisseurs) et colonne « Type » dans le tableau.
- Visualiseur 3D accessible depuis le menu latéral.
- Journal des modifications accessible depuis Paramètres.
- Les PDF s’ouvrent dans une fenêtre aux couleurs du logiciel (pages, zoom, ajustement à la largeur).
- Nouveau visualiseur 3D intégré (STL et OBJ) : rotation, zoom, déplacement, mode solide ou filaire,
  couleur au choix et analyse indicative des parois fines.

- Création de client : recherche d’entreprise sur Internet pendant la saisie du nom (SIRET et adresse repris
  automatiquement), suggestions d’adresse, code client généré automatiquement (initiales, puis AL2, AL3… en cas de doublon) et dossier au format `NOM_CLIENT`.
- Import de fichier depuis la racine d’un projet : choix du dossier de destination (proposé selon le type de fichier) ; depuis un dossier, le fichier y est importé directement.
- Import de fichier : proposition de renommage `nom__RÉFÉRENCE-PROJET.ext` (modifiable), ou conservation du nom d’origine.
- Projets et Clients : le menu d’actions « ⋯ » (ouvrir, modifier, renommer, déplacer, corbeille) est sur chaque ligne
  de la liste ; dans un projet ou un client déjà ouvert il ne reste que « Modifier la fiche ».
- Fichiers d’un projet : action « Ouvrir avec… » (choix de l’application Windows) dans le menu « ⋯ ».
- Aperçu PDF : bouton « Imprimer… » qui ouvre la boîte d’impression de Windows (imprimante, propriétés, exemplaires, pages) ; la page est centrée dans la fenêtre.
- Page Projets : bouton « Créer » ; colonnes des tableaux Projets et Clients alignées sur toutes les lignes.
- Fiche client : bouton « Nouveau projet » (le client est déjà choisi).
- Clients : le code client est attribué automatiquement, affiché en direct dans la fenêtre de création, puis dans une
  colonne « Code » de la liste ; ajout de l’e-mail et du portable (fiche, création, recherche).
- Noms lisibles : « GUY_LALLEMAND » s’affiche « Guy Lallemand », « MPO_FRANCE » « MPO France » (les dossiers ne changent pas).
- Filaments : plus de clic sur la ligne, la modification est dans le menu « ⋯ ».

### Améliorations
- Projets, Clients, Collections, Filaments et Imprimantes s’affichent en tableau compact.
- Les fenêtres de saisie s’ouvrent au centre du logiciel.
- Chaque champ des fenêtres Imprimante et Filament indique son unité (€, h, W, €/kg…).
- Imprimantes : le volume d’impression est grisé pour l’instant et l’option « Imprimante active » est retirée.

### Corrections
- Création d’un client ou d’un projet : plus d’erreur « accès refusé » sur le dossier temporaire (la synchronisation
  Nextcloud retenait brièvement le dossier). Le renommage est réessayé quelques secondes, et l’interface ne se fige plus.
- Création de projet depuis une fiche client : plus d’erreur « le thread appelant ne peut pas accéder à cet objet »
  (même défaut corrigé pour le renommage, les dossiers, les collections et les fiches projet/client).
- Depuis une fiche client, ouvrir un projet affiche bien le projet (la page ne restait plus sur le contact),
  et « Retour » ramène à la fiche client. Les boutons « Créer » et « Actualiser » de la liste des clients ne
  s’affichent plus dans une fiche.
- Le logiciel ne plantait plus au démarrage (onglet Estimations).
- La création d’un projet depuis le tableau de bord ne plante plus.

### Retiré
- L’ancien moteur d’interface WPF a été entièrement supprimé : plus aucune fenêtre de l’ancien logiciel ne s’ouvre.
- « Classer un fichier » dans Paramètres (le classement se fait depuis les projets).

## 7 – 11 octobre 2026

- Refonte complète de l’interface sous Avalonia et .NET 10.
- Gestion des clients, projets, collections, filaments, imprimantes et estimations.
- Corbeille réversible, aperçu des images, documents et modèles 3D.
