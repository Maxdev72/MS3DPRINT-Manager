# Audit simplicité et nombre de clics — 7 novembre 2026

Objectif fixé par l’utilisateur : un logiciel moderne, simple, avec le moins de clics
possible, et qui fonctionne correctement.

Méthode : lecture du code de la fenêtre principale (`MainWindow.axaml` et fichiers
partiels), des fenêtres de saisie et du mode serveur. L’application n’a pas été
manipulée dans sa vraie fenêtre pour cet audit ; les points sont à confirmer à l’écran.

## A. Ce qui ne fonctionne pas correctement (à corriger d’abord)

| # | Constat | Effet pour l’utilisateur | Correction proposée |
|---|---------|--------------------------|---------------------|
| A1 | En mode serveur, la liste « Projet associé » du devis vient des projets **du PC**, pas du serveur (`MainWindow.Estimates.cs:56`). | Un projet créé sur l’autre PC n’apparaît pas dans le devis ; un devis peut être relié à un projet absent du serveur. | Alimenter la liste depuis la copie serveur en mode serveur. |
| A2 | La recherche globale (Ctrl+K) est masquée en mode serveur. | Plus de recherche rapide d’un projet, d’un client ou d’un fichier. | Rechercher dans la copie serveur pour les clients et projets, et sur le disque pour les fichiers. |
| A3 | Les clients utilisés par les Recettes viennent du PC (`MainWindow.Revenues.cs:52`). | Même décalage que A1 pour les factures. | Même correction que A1. |
| A4 | Le formulaire de devis démarre avec 100 g et 2 h déjà remplis. | Un premier devis peut être enregistré avec ces valeurs ; après un enregistrement, une nouvelle saisie est déjà exigée. | Reporté : 17 tests reposent sur ce calcul immédiat. À traiter avec la refonte du devis (B5). |
| A5 | L’échange avec le téléphone est coupé en mode serveur. | L’application Android ne reçoit plus rien. | Publier vers le téléphone depuis la copie serveur. |

## B. Clics et écrans en trop

| # | Constat | Aujourd’hui | Proposé |
|---|---------|-------------|---------|
| B1 | Un bouton « Actualiser » sur 9 pages. | Cliquer pour voir les changements. | Mise à jour automatique (surveillance du dossier, serveur toutes les quelques secondes) et suppression des boutons. |
| B2 | Barre serveur : statut technique de plus de 100 caractères et 4 à 5 boutons (Connexion, Actualiser, Reçu, Déconnexion). | Toujours affichée en haut de chaque page. | Une pastille d’état (« Synchronisé », « Hors ligne », « À vérifier ») ; Connexion et Déconnexion dans Paramètres ; Reçu seulement en cas de problème. |
| B3 | Synchronisation PC → serveur : assistant en 4 à 6 écrans. | À relancer à la main. | Disparaît quand toutes les pages seront sur le serveur. D’ici là, un seul bouton. |
| B4 | Menu de 13 entrées en 3 groupes, plus Notifications et Paramètres. | Beaucoup de choix à chaque ouverture. | Garder 6 entrées principales (Accueil, Projets, Clients, Devis, Recettes, Achats) ; regrouper Imprimantes, Filaments, Collections sous « Atelier », et Modèles 3D, Visualiseur et Corbeille sous « Outils ». |
| B5 | Devis : environ 20 champs sur une page qui défile, dont 6 coûts complémentaires toujours visibles. | Il faut défiler pour atteindre l’essentiel. | Afficher d’abord fichier ou poids, durée, filament et quantité ; replier les coûts complémentaires ; reprendre automatiquement le dernier filament et la dernière imprimante. |
| B6 | Devis : saisie manuelle du poids et de la durée. | Recopie depuis le slicer. | Lire le fichier 3MF ou G-code déposé sur la page (Core sait déjà ouvrir un 3MF Bambu Studio ou OrcaSlicer pour son slicer et ses noms ; il reste à lire le poids et la durée). |
| B7 | Créer un projet depuis un devis : passer par Projets. | Changement de page puis formulaire. | Bouton « Créer le projet » directement dans le devis, client prérempli. |

Suivi du 7 novembre 2026 :

- B1 et B2 : faits (commit 3be7a62).
- B5 : le formulaire était déjà replié en sections (« Ajouter des frais », « Options de fabrication », « Réglages du devis ») ; le constat venait du balisage, réorganisé au démarrage. Ajouté : « Nouveau devis » garde le filament et l’imprimante utilisés.
- B7 : bouton « + » à côté de « Projet associé » ; le projet est créé avec le nom du devis prérempli, puis associé au devis (PC ou serveur).
- A4 : 100 g et 2 h restent affichés comme exemples (calcul immédiat), mais un nouveau devis n’est enregistré qu’après saisie du poids ou de la durée, ou lecture du fichier du slicer.
- B6 : bouton « Lire le fichier du slicer » et dépôt du fichier sur la page : G-code Bambu Studio, OrcaSlicer, PrusaSlicer et Cura (poids calculé pour du PLA 1,75 mm), 3MF tranché (tous les plateaux additionnés). Un projet 3MF jamais tranché est signalé.
- Mode serveur : pages complètes du PC (fichiers, dossiers) avec les clients et projets du serveur ajoutés ; écran de connexion plein écran, adresse ms3dforge.fr fixe.
- Site : page Téléchargements réservée au compte connecté, alimentée depuis Paramètres › « Publier cette version sur ms3dforge.fr ».

## C. Ce qui est déjà bien

- La création de projet tient en un seul écran : client avec recherche, nom et priorité, référence automatique.
- Les suppressions demandent toujours une confirmation, avec Annuler sélectionné par défaut.
- La connexion est mémorisée 30 jours.
- En mode serveur, toutes les pages s’ouvrent sans changer de mode (commit 0b0c6ff).

## Ordre conseillé

1. **A1 à A3** : mêmes clients et projets partout en mode serveur. Petit chantier, gros gain de fiabilité.
2. **A4** : devis sans valeurs fictives.
3. **B1 et B2** : supprimer les boutons Actualiser et alléger la barre serveur. C’est le gain visible immédiat.
4. **B5 à B7** : devis plus court, lecture du fichier du slicer.
5. **B4** : menu resserré.
6. **B3 et A5** : se résolvent en passant les autres pages sur le serveur.
