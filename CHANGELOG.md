# Journal des modifications

## 12 octobre 2026

### Audit : accessibilité et entretien
- Les onglets du visualiseur indiquent à un lecteur d’écran lequel est sélectionné ; la vue 3D est entourée de la couleur d’accent quand elle a le clavier, ce qui montre que les raccourcis (R, W, M, C, P) sont actifs.
- Les vignettes inutilisées depuis trois mois sont supprimées du disque au lancement.

### Image de la vue 3D et vignettes
- Nouveau bouton « Enregistrer une image » (touche `P`) dans la barre d’outils du visualiseur : la vue, telle qu’elle est à l’écran (coupe, comparaison, couleur), est enregistrée en PNG dans le dossier `06_PHOTOS_RENDUS` du projet du modèle ; hors d’un projet, le logiciel demande où l’enregistrer.
- Les fichiers STL et OBJ des projets et des dossiers s’affichent avec une vignette du modèle au lieu d’une icône ; les vignettes sont calculées en arrière-plan, gardées sur le disque jusqu’à ce que le fichier change, et ignorées au-delà de 40 Mo.

### Changer le code d’un client
- Nouveau bouton « Changer le code » dans la fiche client et dans le menu de sa ligne : il propose un code (initiales du nom, ex. FA pour Fell Agency) et montre, à mesure que vous tapez, les dossiers de projet qui seront renommés (F-2026-001_SAMURAI_70CM devient FA-2026-001_SAMURAI_70CM).
- Au changement, les dossiers, les fiches projet, les références réservées et les estimations liées suivent ; en cas d’erreur tout est remis en l’état. Rien n’est modifié avant la confirmation.

### Visualiseur 3D : rotation et découpage
- La rotation à la souris est inversée sur l’horizontale : faire glisser vers la droite tourne le modèle vers la droite.
- Les imprimantes à deux buses (H2D) ne peuvent pas être découpées en ligne de commande (erreur « multi-extruder ») : le logiciel découpe une H2D comme une H2S, à une seule buse, et l’indique ; un message clair remplace l’erreur du découpeur.

### Visualiseur 3D : nouvelle interface
- La vue 3D reste au centre ; une barre d’outils flottante en haut à gauche regroupe recentrer, filaire, mesurer, couper et comparer, chacun avec son icône et son raccourci.
- Le panneau de droite passe de neuf sections empilées à quatre onglets : Infos (modèle, unités, résultats, matière, durée d’impression), Affichage, Outils (réglages de l’outil actif) et Analyse.
- Les résultats sont présentés en lignes alignées, durée, poids et coût d’abord, puis volume, surface et filament ; le bouton « Créer une estimation avec ce modèle » reste toujours visible en bas du panneau.
- Activer un outil ouvre son onglet ; les réglages d’un outil inactif ne s’affichent plus.

### Accents des noms de projets et de clients
- Les noms de dossier perdent leurs accents (« EPEE_FOURREAU_REPLIQUE ») : l’affichage les retrouve pour les mots usuels (« Épée Fourreau Réplique », « Pions d’Échecs », « MV Créations », « Gâchette de Tronçonneuse »). Les dossiers sur le disque ne sont pas renommés.
- Un projet créé depuis le logiciel garde désormais le nom tel que saisi (accents, espaces, majuscules) dans sa fiche, au lieu de la version normalisée du dossier ; la fiche est créée dès la création pour un client qui a une fiche.
- « Modifier la fiche » propose le nom du projet lisible et permet de le corriger ; « Compléter la fiche » d’un projet historique propose le nom avec ses accents.

### Position de la fenêtre mémorisée
- La fenêtre se rouvre à l’endroit où vous l’avez laissée, sur le bon écran, avec sa taille ; fermée en plein écran, elle se rouvre en plein écran et revient à sa position d’avant en la réduisant.
- Si cet endroit n’est plus visible (écran débranché, résolution changée), elle est recentrée sur l’écran principal.

### Comparaison de deux versions
- « Comparer avec une autre version » charge un second fichier et le superpose au premier dans une autre couleur (rouge sur bleu) : les zones rouges montrent où la version B dépasse A, les bleues où A dépasse B.
- Tableau des écarts en mm : largeur, profondeur, hauteur, volume, surface et nombre de triangles ; affichage de A ou de B seul, alignement des centres.
- La comparaison utilise le rendu par carte graphique (le logiciel y repasse automatiquement).

### Plan de coupe
- Nouvelle section « Plan de coupe » (touche `C`) : une coupe horizontale (hauteur) ou verticale selon X ou Y, réglée au curseur, avec le choix du côté conservé et la position affichée en mm.
- La face coupée apparaît en orange pour lire les parois et les cavités ; mesures et sélection de points ignorent la partie coupée.
- Les boutons à bascule, curseurs et cases à cocher reprennent la couleur d’accent de l’application.

### Unités du modèle mémorisées
- Un seul choix d’unité (mm, cm, m, pouces ou personnalisée) remplace l’échelle à saisir et la case de confirmation ; il est mémorisé pour chaque fichier.
- Les dimensions s’affichent dans l’unité choisie et en mm, une alerte signale un modèle de taille improbable, et mesures, volume, poids, analyse des parois et découpage suivent l’unité.
- Le découpeur reçoit l’échelle : un modèle dessiné en pouces est découpé à la bonne taille.

### Découpage automatique (Bambu Studio / OrcaSlicer)
- Le logiciel détecte Bambu Studio ou OrcaSlicer et découpe le modèle ouvert dans le visualiseur sans afficher sa fenêtre : durée d’impression, longueur de filament et poids réels sont affichés au chargement.
- L’imprimante (profil machine) est choisie parmi vos imprimantes du logiciel, la qualité parmi les profils compatibles, et le filament selon la matière ; le remplissage et les parois du panneau sont transmis au découpeur.
- « Créer une estimation avec ce modèle » reprend maintenant la durée machine, le poids exact, l’imprimante et le filament : on passe du fichier au prix en un clic.

### Du visualiseur à l’estimation
- Bouton « Créer une estimation avec ce modèle » : ouvre une nouvelle estimation préremplie avec le nom du fichier, le poids estimé, le filament choisi et le projet auquel appartient le fichier.
- Une nouvelle estimation reprend le taux horaire de l’imprimante sélectionnée au lieu d’une valeur par défaut.

### Visualiseur 3D : volume et poids
- Le panneau indique le volume et la surface du modèle, puis le poids estimé selon la matière (PLA, PETG, ABS, ASA, TPU, Nylon, PC, PVA ou l’un de vos filaments), le remplissage et l’épaisseur des parois.
- Avec un filament de votre catalogue, le coût de matière est calculé ; un maillage ouvert est signalé.
- Correction : la rotation, le déplacement, le zoom et les clics de mesure fonctionnent de nouveau avec le rendu par carte graphique.

### Visualiseur 3D : mesures
- Nouvel outil « Mesurer des distances » (touche `M`) : cliquez deux points du modèle, la longueur s’affiche sur la vue et dans le panneau avec ses écarts en X, Y et Z.
- Les points s’accrochent aux sommets proches ; plusieurs mesures peuvent être gardées, `Échap` annule le point en cours.
- Les longueurs sont exprimées en mm avec l’échelle (mm par unité du modèle) indiquée dans le panneau.

### Visualiseur 3D : finition
- Le menu « Visualiseur 3D » est mis en évidence quand il est ouvert ; bouton « Ouvrir un fichier » au centre de la vue vide.
- Pastilles de couleur avec anneau de sélection, chemin du fichier abrégé (infobulle complète), résultat de l’analyse des parois résumé en une ligne avec le détail dessous.

### Visualiseur 3D accéléré
- Rendu par la carte graphique (OpenGL via ANGLE) : le modèle reste sur le GPU, rotation fluide même avec plusieurs millions de triangles, bords lissés (multi-échantillonnage 4x), points d’analyse des parois affichés par le GPU.
- Choix du rendu (carte graphique ou processeur) dans le panneau ; bascule automatique vers le processeur si aucun contexte graphique n’est disponible.
- Le visualiseur occupe toute la largeur de la fenêtre.

### Visualiseur 3D intégré
- Le visualiseur 3D s’ouvre dans la fenêtre du logiciel (plus de fenêtre séparée), avec un bouton Retour qui ramène à la page précédente.
- Vue plus grande, panneau latéral (modèle, affichage, analyse), pastilles de couleur, raccourcis `R` (recentrer) et `W` (filaire), dépôt de fichier directement sur la vue.

### Dossiers modifiables
- Dans le contenu d’un dossier de collection : **Nouveau dossier**, **Importer un fichier**, et pour chaque ligne un menu « ⋯ » (renommer, déplacer, corbeille, ouvrir avec…).
- Dans un projet, les sous-dossiers ont aussi leur menu d’actions.
- Ouvrir un dossier remplace la page affichée au lieu de se superposer à elle.

### Audit accessibilité et robustesse
- Boutons à icône et lignes de listes nommés pour les lecteurs d’écran ; boutons d’action agrandis (36 px).
- Textes de 10 et 11 px portés à 12 px ; couleurs restantes passées en jetons de thème.
- La page Estimations et le choix de dossier d’import ne figent plus la fenêtre pendant la lecture du disque.
- Les animations sont coupées quand Windows demande moins d’animations ; l’étape terminée de l’estimation affiche une icône dessinée.
- Ajout de `PRODUCT.md` et `DESIGN.md` pour cadrer les prochaines évolutions.

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
