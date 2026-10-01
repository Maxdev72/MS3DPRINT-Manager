# Visualiseur 3D fluide, filaire et couleurs — conception

## Objectif

Rendre la rotation et le zoom des STL/OBJ sensiblement plus fluides sur les maillages volumineux, sans modifier les fichiers source. Ajouter un mode filaire et un choix de couleur. Conserver un exécutable Windows x64 portable, autonome et sans dépendance payante.

## Constat et stratégie

Le visualiseur actuel importe les modèles via `HelixToolkit.Wpf.ModelImporter`, puis affiche toute la géométrie dans `HelixViewport3D` (WPF 3D). Le chargement est déjà hors du thread UI, mais aucun niveau de détail ni mesure du coût d'interaction n'existe. Le poste observé dispose d'une NVIDIA RTX 5070 et du niveau WPF Tier 2 ; une absence générale de GPU n'explique donc pas à elle seule le ralentissement. Aucun fichier utilisateur représentatif n'a été fourni : la cause précise sera vérifiée sur un STL synthétique dense, puis sur un modèle réel si disponible.

La variante `HelixToolkit.Wpf.SharpDX` 3.1.2 apporte un moteur DirectX 11 et un mode de remplissage filaire. Elle est compatible avec le projet .NET 8. La migration sera limitée au visualiseur, sans changer les catalogues, les formats ni le stockage. L'ancien rendu WPF restera disponible comme solution de repli tant que la nouvelle voie n'est pas validée sur le poste cible. Un simple échec de création DirectX doit afficher une erreur exploitable ou activer le repli, jamais fermer toute l'application.

Références : [architecture HelixToolkit](https://github.com/helix-toolkit/helix-toolkit), [paquet WPF SharpDX 3.1.2](https://www.nuget.org/packages/HelixToolkit.Wpf.SharpDX/3.1.2), [exemple filaire](https://github.com/helix-toolkit/helix-toolkit/issues/1001), [signalement ZoomExtents 3.1.2](https://github.com/helix-toolkit/helix-toolkit/issues/2467).

## Comportement utilisateur

- Ouverture par les actions existantes, glisser-déposer et boîte de dialogue ; STL binaire/ASCII et OBJ restent acceptés.
- Barre compacte dans le visualiseur : `Solide` / `Filaire`, palette de couleurs et saisie hexadécimale validée. La couleur s'applique à l'affichage, pas au fichier. Le choix reste actif lors du chargement d'un autre modèle dans la même fenêtre.
- Indication du nombre de triangles, du temps de chargement et du mode de rendu. Aucun traitement au fil de la rotation ; le changement de mode/couleur ne doit pas relire le fichier.
- Les dimensions et le recentrage restent disponibles. Préserver l'orientation de la caméra quand le mode ou la couleur change. Ne pas utiliser aveuglément `ZoomExtents` de SharpDX 3.1.2 : un problème de caméra est signalé en amont ; le cadrage sera testé et, si nécessaire, calculé depuis les bornes du modèle.
- Si le modèle est trop lourd pour une interaction correcte, proposer un mode `Rapide` qui réduit uniquement la géométrie affichée, avec retour explicite au modèle complet. Ne pas simplifier ni réenregistrer la source. Le seuil et la méthode seront définis après le benchmark initial, avec tests préservant bornes et topologie visible autant que possible.

## Architecture

`ModelPreviewWindow` garde l'orchestration UI. Un chargeur isolé lit STL/OBJ en arrière-plan et retourne une scène de prévisualisation (géométries, matériaux éventuels, bornes, nombre de triangles). La vue GPU projette cette scène dans `Viewport3DX` ; la vue WPF actuelle reste le repli. Les paramètres d'affichage (mode, couleur, qualité) sont distincts des données du modèle. Les changements de paramètres réutilisent la scène en mémoire. Chaque ressource DirectX est libérée à la fermeture ou au remplacement de la scène.

Le nombre de triangles est borné pour l'affichage en mode rapide, sans perte de coordonnées du fichier source. Les chargements concurrents conservent la logique de version existante : seul le dernier fichier demandé peut remplacer la scène visible. Les erreurs de parsing, de mémoire ou de périphérique GPU sont affichées sans fermer l'application.

## Validation

Tests automatisés : STL binaire et ASCII, OBJ, bornes/dimensions, nombre de triangles, sélection du dernier chargement, couleur hexadécimale invalide, bascule solide/filaire sans relecture du fichier, création de la fenêtre STA. Benchmark reproductible sur plusieurs tailles de maillage synthétique : temps de chargement et temps de rendu/interactions observés avant/après, sans seuil de FPS arbitraire dans la suite CI. Vérification manuelle en rotation/zoom, thèmes clair/sombre, glisser-déposer, recentrage, fermeture/réouverture, et publication portable. La nouvelle version ne remplace l'exécutable installé qu'après tests et essai de lancement ; une sauvegarde de l'ancienne version reste récupérable.
