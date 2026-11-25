# Manipulation directe des pièces dans le rendu

Objectif : déplacer et tourner la pièce sélectionnée directement dans l’aperçu, sans redimensionnement.

Le gizmo reste dans `Rendering/OrientationGizmo.cs` et partage la projection et les rayons du visualiseur. Les flèches contraignent le déplacement à X/Y/Z, les poignées carrées à un plan, les anneaux à une rotation. Les changements sont visibles pendant le glissement ; les fichiers Blender sont actualisés au relâchement. L’outil est activable et masquable dans la vue.

## Étapes

- [x] Tester les flèches, plans, rotations et la fin de glissement avec la souris simulée.
- [x] Étendre le gizmo avec des déplacements en millimètres et des poignées identifiables.
- [x] Stabiliser le pivot des pièces et le cadrage : déplacer une pièce ne recentre pas la caméra ; tourner une pièce ne déplace pas les autres.
- [x] Conserver les positions X/Y/Z et pivots dans les scènes, avec lecture des anciennes scènes à deux coordonnées.
- [x] Intégrer l’outil dans la vue et simplifier le panneau autour de la sélection et des réglages précis.
- [x] Vérifier les tests de rendu, les anciennes scènes et le fonctionnement au clavier, puis capturer l’interface.

Revue : écrêtage GPU adapté à la géométrie déplacée sans recentrage ; sol Blender fixé en coordonnées monde pour les pièces soulevées. Vérifications dédiées : matrices GPU et inspection du sol dans Blender avec un produit soulevé, en conservant le comportement historique quand la référence de sol est absente.

Contraintes : thème existant, textes français, confirmations de retrait conservées, aucune mise à l’échelle, aucune publication sans accord.
