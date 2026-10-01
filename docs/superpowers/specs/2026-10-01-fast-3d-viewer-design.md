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

`GpuModelPreviewWindow` orchestre le nouveau rendu. Un chargeur isolé lit STL/OBJ en arrière-plan et retourne une scène de prévisualisation (géométries, bornes, nombre de triangles). Pour les STL binaires à taille canonique, le lecteur SharpDX évite de dupliquer tout le maillage. Pour les STL texte et OBJ, l'importeur WPF déjà utilisé reste la voie compatible, puis ses géométries sont converties pour `Viewport3DX` ; cette étape coûte davantage de mémoire pendant le chargement, mais pas durant la navigation. `ModelPreviewWindow` reste le repli. Les paramètres d'affichage sont distincts des données du modèle : mode et couleur réutilisent la scène en mémoire. Un seul import est autorisé à la fois et les demandes périmées sont annulées en attente ou durant la conversion ; l'importeur tiers lui-même ne peut pas être interrompu en cours de lecture. Chaque ressource DirectX est libérée à la fermeture ou au remplacement de la scène.

Le mode rapide avec simplification visuelle demeure une extension possible après mesure sur un fichier réel représentatif ; aucun fichier source ne sera simplifié ou réenregistré. Les chargements concurrents conservent la logique de version existante : seul le dernier fichier demandé peut remplacer la scène visible. Les erreurs de parsing ou de périphérique GPU sont affichées sans fermer l'application, et le rendu WPF reste accessible.

## Validation

Tests automatisés : STL binaire et ASCII, OBJ, bornes/dimensions, nombre de triangles, sélection du dernier chargement, couleur hexadécimale invalide, bascule solide/filaire sans relecture du fichier, création de la fenêtre STA. Benchmark reproductible sur plusieurs tailles de maillage synthétique : temps de chargement et temps de rendu/interactions observés avant/après, sans seuil de FPS arbitraire dans la suite CI. Vérification manuelle en rotation/zoom, thèmes clair/sombre, glisser-déposer, recentrage, fermeture/réouverture, et publication portable. La nouvelle version ne remplace l'exécutable installé qu'après tests et essai de lancement ; une sauvegarde de l'ancienne version reste récupérable.

Mesure initiale sur le poste cible (2026-10-01, fichier STL binaire synthétique) : import de 10 000 triangles en 19 ms, import de 100 000 triangles en 75 ms ; rotation automatisée du rendu DirectX sur 100 000 triangles : 62,6 images/s observées. Ce chiffre ne garantit pas la même fluidité sur un modèle client réel ; les maillages et matériaux diffèrent.
