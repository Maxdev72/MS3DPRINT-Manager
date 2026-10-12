# Produit — MS3DPRINT Manager

> Rédigé à partir du code et de l’historique du projet (pas d’entretien utilisateur). À corriger si une hypothèse est fausse.

## Ce que c’est
Logiciel de bureau Windows (C# / .NET 10 / Avalonia) qui organise l’activité d’un atelier d’impression 3D :
clients, projets (dossiers et fichiers rangés par référence), collections, filaments, imprimantes,
estimations de prix et aperçus de fichiers (images, PDF, STL/OBJ).

## Utilisateur
Une personne qui tient seule un atelier d’impression 3D / modélisation et prépare devis et dossiers clients.
Elle travaille longtemps devant l’écran, sur grand écran, souvent en lumière d’atelier : thème sombre.
Tout est en français.

## Mode
**Operate** : l’interface sert à accomplir des tâches (créer, retrouver, ranger, chiffrer). La lisibilité,
la cohérence et la vitesse passent avant l’expression.

## Principes
- Rien n’est détruit : corbeille réversible, aucun écrasement à destination.
- Le logiciel propose (code client, renommage de fichier), l’utilisateur décide.
- Une erreur ne ferme jamais le logiciel : message clair, journal dans `%LOCALAPPDATA%`.
- Les listes sont denses et triables ; chaque ligne s’ouvre d’un clic, ses actions sont dans un menu « ⋯ ».

## À éviter
Cartes imbriquées, indicateurs décoratifs, liserés colorés, symboles texte en guise d’icônes,
animations qui retardent une action.
