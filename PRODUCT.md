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

## Accès connecté — direction validée le 3 novembre 2026

L’utilisateur prépare une distribution à sa clientèle. Le logiciel doit pouvoir
se connecter directement à un atelier serveur depuis son interface habituelle,
sur PC, web et Android, avec un fonctionnement indépendant du PC allumé.
Cette direction ne signifie pas que l’isolation de comptes clients est déjà livrée.

Le parcours approuvé pour le PC est une connexion intégrée au lancement, puis
les pages usuelles Accueil, Clients et Projets sur la référence serveur. L’état
de connexion et le compte restent visibles ; la dernière copie est consultable
hors connexion et les écritures restent bloquées. La session mémorisée utilise
le coffre Windows, jamais un mot de passe stocké dans le dossier de l’atelier.

L’atelier local historique conserve ses fonctions et s’ouvre par un choix
explicite. Une coupure ne doit jamais réactiver ses écritures comme si elles
étaient synchronisées. Les modules non portés sont signalés dans le mode
serveur. La destination commerciale complète reste la parité des fonctions ;
les lots intermédiaires doivent nommer précisément leurs limites.

Précision utilisateur du 4 novembre : les données PC sont plus récentes que
la copie serveur. Le premier lancement sur un atelier existant sans choix de
mode enregistré doit donc rester local. L’ouverture explicite du serveur ne
réalise pas leur transfert. Il faut sauvegarder et comparer les évolutions PC
avant une nouvelle migration vérifiée ; aucune copie serveur ancienne ne doit
être présentée comme l’état le plus récent de l’atelier PC.
