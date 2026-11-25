# Rendu produit — prochaines étapes (plan validé le 25 novembre 2026)

État de départ : version 1.0.27 (caméra à la souris, aperçu EEVEE en direct, Blender gardé ouvert,
HD Cycles GPU, HDRI, couleurs perso, boutons « Basculer »). Fichiers : `src/MS3DPRINT.Manager.Desktop/Rendering/`
(`RenderStudioView.cs`, `ProductRenderer.cs` avec `BlenderSession` et `RenderCamera`, `RenderPreferences.cs`,
`product_render.py`), `MainWindow.Render.cs`, tests `tests/.../Desktop/ProductRendererTests.cs` et
`tests/.../Estimates/RenderPageUiTests.cs`. Mesures sur le PC (RTX 5070) : EEVEE 960 px ≈ 0,13 s,
Cycles 960 px ≈ 1,7 s, HD 2048 px 256 éch. ≈ 18 s GPU / 125 s CPU, démarrage Blender ≈ 2,4 s.

Règles : demander confirmation pour tout retrait (`ConfirmRemovalWindow`), textes en français,
grille d'espacement 4 px (`DesignTokenTests`), incrémenter la version à chaque livraison (tag `vX.Y.Z`).

## Livraison 1 — Onglets et contenu (1.0.28)

1. **Onglets façon KeyShot** dans le panneau droit : Pièce, Matière, Ambiance, Caméra, Rendu (chips
   `filter-chip` comme le visualiseur 3D). « Rendu HD » et « Enregistrer l'image » toujours visibles en bas.
   Dernier onglet retenu (`RenderPreferences`). Supprimer la barre de vues au-dessus de l'image.
2. **Orientation (onglet Pièce)** : gizmo à anneaux X/Y/Z dessiné en surimpression de la vue 3D
   (projection via `ViewMath.TryProject`), glisser = rotation autour de l'axe ; pas au choix libre / 5 / 15 / 45 / 90°.
   Redressement automatique à l'ouverture : poser sur la plus grande famille de faces planes
   (réutiliser la logique de `AutoOrient.FaceFamilies` ou `ModelOrientation.FaceDown`), bouton « Annuler le redressement ».
   « Poser sur une face » (clic sur la pièce, `MeshPicker.TryPick` + `FaceDown`), « Orientation du fichier ».
   Orientation retenue par fichier (clé = chemin du modèle) dans `RenderPreferences`.
   Remplace les boutons « Basculer ». `_turn` (Quaternion) existe déjà.
3. **Couleurs (onglet Matière)** : croix au survol de chaque pastille pour retirer (confirmation), y compris
   couleurs de base (liste des couleurs masquées dans les préférences) ; bouton + inchangé.
4. **Finitions** : libellés « Mat », « Satiné / soie », « Brillant » (clés `matte`, `silk`, `glossy` inchangées),
   plus « Phosphorescent » (clé `glow` : teinte claire + légère émission dans `product_render.py`, option
   « Dans le noir » qui éteint les lumières pour montrer la lueur).
4 bis. **Palettes de filament** : `Rendering/filament-palettes.json` (déjà dans le dépôt, à déclarer en
   `EmbeddedResource`) = 4 groupes Mat (25), Brillant (30), Soie (13), Phosphorescent (5), chacun avec sa finition.
   Onglet Matière : pastilles rangées par groupe repliable ; un clic applique couleur + finition du groupe.
   Ordre : vos couleurs, filaments de l'atelier, puis ces palettes ; doublons (même hex + finition) fusionnés ;
   les anciennes couleurs de base (`FilamentColors`) sont remplacées par ces palettes. Les codes sont des
   estimations depuis des captures : le dire dans l'info-bulle (« teinte approchée »).
5. **Ombres (onglet Ambiance)** : ombre au sol oui/non, intensité, douceur (taille des lumières), direction
   (angle + hauteur : tourner les trois lumières ensemble dans `product_render.py`), ombre de contact
   (AO / `eevee` et Cycles). Nouveaux champs dans `RenderSettings` + script. Avec un HDRI, pas de lumières
   studio : l'ombre suit la rotation de l'environnement (la direction se règle par cette rotation).
6. **Caméras (onglet Caméra)** : liste vues prédéfinies + caméras enregistrées (nom, yaw, pitch, zoom, pan,
   champ de vision), ajouter / renommer / mettre à jour / retirer (confirmation), verrou caméra (ignorer la souris),
   champ de vision (focale : passer `fieldOfView` à `RenderCamera` et au viewport), format, « Recentrer ».
   Caméras retenues par pièce. Cases à cocher pour les séries (livraison 3).

## Livraison 2 — Performances (1.0.29)

7. **Aperçu progressif façon KeyShot** : pendant le mouvement EEVEE 480 px ; à l'arrêt, paliers Cycles
   8 → 32 → 128 échantillons, débruitage GPU (`cycles.denoising_use_gpu`), chaque palier remplace le précédent,
   repart au moindre changement (boucle `_generation` existante). Montre l'ombre sur fond transparent/HDRI.
8. **Optimisation** : lancer `BlenderSession` à l'ouverture de la page (warm-up + compilation shaders),
   ne plus reconstruire la scène à chaque job (mettre à jour caméra/matière/lumières, `use_persistent_data`),
   échantillonnage adaptatif, rebonds réduits, maillage allégé pour le direct sur gros STL,
   pause de l'aperçu et de la vue 3D pendant un rendu HD ou une série. Mesurer avant/après chaque piste.

## Livraison 3 — Rendu et finitions (1.0.30)

9. **Qualité** : Rapide (EEVEE), Standard (Cycles 1024 px, défaut des séries), HD (2048 px), Très haute (4096 px).
10. **CPU ou GPU** : Automatique / Carte graphique (nom détecté) / Processeur ; passer le choix au script
    (`use_graphics_card` conditionnel). EEVEE reste sur GPU.
11. **Séries de rendus** : caméras cochées rendues à la suite, rangées dans le dossier photos du projet
    (`SaveViewerCapture`), nom « pièce - caméra.png », bouton Annuler.
11 bis. **Rendus en plusieurs couleurs (variantes)** : sélection multiple de pastilles (Ctrl+clic, ou « tout le
    groupe »), même pièce / caméra / ambiance rendue dans chaque couleur, fichiers « pièce - Rouge mat.png ».
    Option « Planche couleurs » : une seule image en grille avec toutes les variantes et leur nom.
    Combinable avec les caméras cochées (couleurs × caméras, total affiché avant de lancer, ex. 5 × 3 = 15 images
    ≈ 1 min 15 en Standard). Blender reste ouvert et le modèle déjà importé : seule la matière change entre deux images.
12. **Avancement** : vérifier d'abord si Blender écrit « Sample x/y » sur stdout en mode serveur
    (sinon estimation d'après les images précédentes) ; afficher « Image 2/5 · 47 % · ~1 min ».
13. **Format et compression** : PNG (défaut, seul avec transparence), JPEG, WebP, TIFF 16 bits ;
    compression PNG max, curseur de qualité JPEG/WebP avec poids estimé, taille de sortie réduite facultative,
    métadonnées retirées. Avertir si fond transparent + JPEG.
14. **Notification « Image enregistrée »** (`SaveViewerCapture` dans `MainWindow.Viewer.cs`) : bouton
    « Ouvrir le dossier » (Explorateur avec le fichier sélectionné) et nom du fichier seul.
