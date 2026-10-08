# Parcours client, projet et fichiers — conception partagée

## Intention

Réduire les clics et rendre le parcours évident. Un utilisateur doit pouvoir partir d'un client ou d'un projet, créer l'élément manquant sans changer de contexte, puis choisir de travailler dans les fichiers ou de revenir à l'accueil.

## Création depuis Nouveau projet

L'assistant présente les étapes **Client**, **Projet** et **Confirmation**. L'étape Client permet de rechercher ou choisir un client complet. Le bouton **Nouveau client** ouvre la création dans le parcours ; après enregistrement, il revient avec ce client sélectionné et conserve le brouillon du projet. L'étape Projet saisit année, nom, échéance facultative et description, puis prévisualise la référence et le dossier. La confirmation crée le projet.

Après création, l'assistant propose exactement **Ouvrir le projet** et **Retour à l'accueil**. L'import de fichiers n'est jamais imposé. L'annulation ne crée aucun dossier ni profil.

## Création depuis Nouveau client

Après l'enregistrement d'un nouveau client, afficher **Créer un projet maintenant** et **Retour à l'accueil**. Le premier choix utilise le même assistant, directement à l'étape Projet, avec le client pré-sélectionné. L'édition d'un client existant garde son comportement actuel.

## Projet et fichiers

La fiche projet commence sur l'onglet Fichiers. L'utilisateur peut importer plusieurs fichiers et choisir leur dossier de destination dans l'arborescence du projet, sans quitter la fiche. Les fichiers STL, OBJ et 3MF restent visualisables depuis la sélection. Ouvrir, renommer, déplacer et supprimer restent des actions sur la sélection avec la corbeille et les protections de chemins existantes.

La taille d'un fichier s'affiche immédiatement. La taille cumulée d'un dossier est calculée en arrière-plan : l'ouverture de la liste ne doit jamais attendre ce calcul.

## Ergonomie

- En-tête projet compact : retour, référence, nom, statut et actions regroupées.
- Onglets Fichiers et Informations courts et alignés à gauche ; Informations dans une carte de largeur lisible.
- Fil d'Ariane cliquable au lieu du chemin complet, barre d'actions regroupée par navigation, sélection et ajout de contenu.
- Une seule zone de défilement vertical par contenu ; horizontal seulement lorsqu'un tableau dépasse réellement.
- Animations courtes et compatibles avec les quatre thèmes pour les étapes, détails et confirmations ; elles ne retardent jamais une commande.

## Tableau de bord

Conserver Créer un client, Créer un projet, projets récents ou actifs et alertes actionnables (retard, fiche incomplète, rangement, corbeille). Les modules moins fréquents restent dans la navigation latérale. Retirer les cartes décoratives et informations répétées.

## Critères d'acceptation

- Aucune donnée n'est créée par consultation ou annulation.
- Les règles existantes imposant un client complet avant création de projet restent appliquées.
- Brouillon et sélection client survivent aux changements d'étape.
- Le parcours, les thèmes, le défilement et les tailles sont testés sur données temporaires.
- La livraison reste un seul `MS3DPRINT Manager.exe`, avec contrôle SHA256 avant remplacement.
