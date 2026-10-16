# Visualiseur 3D type découpeur : réorientation et plateau d'impression

Date : 16 octobre 2026. Statut : conception validée par l'utilisateur, spécification à relire avant le plan d'implémentation.

## 1. But

Faire du visualiseur 3D un espace de préparation d'impression, comme un découpeur : la pièce est posée sur un **plateau d'impression**
aux dimensions d'une imprimante, on peut la **réorienter** (poser sur une face, tourner, orientation automatique) et tout ce que le
logiciel calcule (poids, durée, supports, faisabilité, découpe Bambu) suit l'orientation choisie.

Pourquoi : l'orientation change la matière de supports, la durée et la réussite d'une impression, donc le prix d'un devis. C'est aussi
la brique qui manque à l'API de devis (`docs/api-devis-en-ligne-plan.md`, paragraphe 11).

## 2. Décisions prises avec l'utilisateur

| Sujet | Décision |
|---|---|
| Périmètre | plateau et réorientation ensemble, en un seul chantier |
| Dimensions du plateau | imprimantes de l'atelier (champ « volume d'impression » de la page Imprimantes) ou saisie à la main ; sans choix, 256 × 256 × 256 mm |
| Moyens de réorienter | poser sur une face (clic), boutons ±90° et champs d'angles, orientation automatique. **Pas** de poignée circulaire à glisser |
| Aspect | grille infinie conservée ; plateau en rectangle plus clair avec contour et dimensions, rouge si la pièce déborde |
| Sortie | le fichier d'origine n'est jamais modifié ; calculs et découpe sur la pièce tournée ; bouton « Enregistrer le modèle orienté » (copie STL en mm) |
| Mémorisation | l'orientation n'est pas mémorisée par fichier |

Hors périmètre : plusieurs pièces sur le plateau, mise en page automatique, rotation par anneaux à la souris, plateaux ronds ou
avec zones interdites, mémoire de l'orientation entre deux ouvertures.

## 3. Principe : une copie transformée

Le visualiseur garde `_original` (le modèle tel que chargé, en unités du fichier) et un état d'orientation. Il en dérive `_mesh`, la
pièce **tournée puis posée sur le plateau**, que tout le code existant utilise déjà (affichage, mesures, coupe, estimation, faisabilité,
découpe). Aucun de ces consommateurs ne change.

Écartées : appliquer une matrice seulement à l'affichage (chaque calcul devrait refaire la transformation : deux sources de vérité),
modifier le modèle chargé (irréversible).

## 4. Unités et interfaces

### 4.1 `ModelOrientation` (Desktop/Viewer, logique pure)

État : une rotation `Quaternion`. Opérations, toutes sans interface graphique :
- `RotateAxis(axe, degrés)` : compose une rotation autour d'un axe du plateau (X, Y ou Z).
- `FaceDown(normale)` : tourne la pièce pour que la normale (en coordonnées de la pièce **telle qu'affichée**) pointe vers −Z ; une normale de longueur nulle ou non finie est ignorée.
- `ApplyLike(référence, autre)` : place un second modèle exactement comme le premier (utilisé par la comparaison de versions).
- `Reset()`, `EulerDegrees` (lecture, pour les champs d'angles), `SetEuler(x, y, z)`.
- `Apply(MeshModel original) -> MeshModel` : positions tournées autour du centre de la boîte englobante, centrées sur l'origine en X et Y,
  base à Z = 0. Les triangles sont inchangés (une rotation propre conserve l'orientation des faces).
- Seule `Apply` est coûteuse (une passe sur les sommets) ; les autres opérations ne touchent que le quaternion.

### 4.2 `PlateSize` (Desktop/Viewer, logique pure)

`record PlateSize(double WidthMm, double DepthMm, double HeightMm)` ; `PlateSize.Parse(string?)` lit « 256 × 256 × 256 mm »,
« 256x256x256 », « 256*256*256 », virgule ou point décimal, ordre largeur × profondeur × hauteur ; renvoie `null` si illisible
(plateau rond, texte libre) ; `PlateSize.Default` = 256 × 256 × 256. `Fits(Vector3 sizeMm)` et `Overflow(...)` pour l'alerte, avec une tolérance de 0,01 mm utilisée comme seuil : un écart jusqu'à 0,01 mm compte comme « tient », un écart plus grand est rapporté en entier.

### 4.3 `AutoOrient` (bibliothèque `MS3DPRINT.Estimator`)

```
OrientationProposal AutoOrient.Propose(PrintMesh mesh, EstimateSettings settings, MachineProfile? machine,
                                       AutoOrientGoal goal, Vector3? plateMm, CancellationToken token)
```
- **Candidats** : les 6 directions d'axes et les 12 plus grandes familles de faces (normales regroupées à 5°, pondérées par l'aire),
  chacune posée vers le bas.
- **Note** de chaque candidat, avec le moteur : matière de supports, durée, surface de contact de la base (stabilité), hauteur,
  dépassement du plateau. Un candidat qui déborde est écarté ; une base de moins de 20 mm² est pénalisée.
- `AutoOrientGoal` : `FewestSupports` (défaut), `Fastest`, `MostStable`.
- Égalité : un meilleur candidat à moins de 0,02 de la note normalisée de l'orientation actuelle laisse la pièce telle quelle. Le drapeau `Limited` de la proposition indique seulement que, au-delà d'un million de triangles, seuls les six axes ont été essayés.
- Résultat : la rotation (`Quaternion`), les chiffres du meilleur candidat et de l'orientation actuelle, pour afficher le gain
  (« −2,9 cm³ de supports, −12 min »). Asynchrone côté interface, annulable, avec progression.
- Pure et sans dépendance graphique : réutilisable par l'API.

### 4.4 Outil « Poser sur une face » (ModelViewerView)

Une entrée de la barre d'outils, comme la règle. Un clic lance `ViewMath.Ray` + `MeshPicker.TryPick` (existants) ; la normale du
triangle touché alimente `ModelOrientation.FaceDown`. Pas de surlignage au survol : le résultat est visible tout de suite.
Échap quitte l'outil.

### 4.5 Plateau à l'écran

Dans `GpuMeshViewport` : nouveaux paramètres `PlateHalfWidth`, `PlateHalfDepth`, `PlateOverflow`. Le shader de la grille dessine le
rectangle, dessiné seulement quand la grille est affichée (fond un peu plus clair, contour d'un pixel et demi calculé avec `fwidth`, rouge si `PlateOverflow`) ; la grille infinie reste
autour, plus discrète. Le mode « Logiciel (processeur) » ne dessine pas de grille, donc pas de plateau (comme aujourd'hui).

La liste des imprimantes : `ModelViewerView` reçoit, en plus des noms, leur `BuildVolume` (un nouveau paramètre de constructeur `loadPrinters` qui renvoie des `ViewerPrinter(Name, BuildVolume)`). Le choix
« Plateau » liste les imprimantes dont le volume est lisible, plus « Saisir… » et « Générique 256 ».

### 4.6 Sortie et découpe

- `StlWriter.Write(path, MeshModel, scaleToMm)` : STL binaire en millimètres (nouveau, testé).
- Découpe Bambu : si l'orientation n'est pas l'identité, un STL temporaire en millimètres de la pièce tournée remplace le fichier
  d'origine (et `--scale` n'est plus passé) ; supprimé après la découpe.
- « Enregistrer le modèle orienté » : boîte de dialogue d'enregistrement, nom proposé `<nom>__orienté.stl`, dans le dossier du modèle.
- « Créer une estimation avec ce modèle » continue d'envoyer le fichier d'origine ; le poids et la durée sont ceux de l'orientation courante.

## 5. Flux de données

```
fichier ──► MeshLoader ──► _original ─┐
                                      ├─► ModelOrientation.Apply ──► _mesh ──► affichage / mesures / coupe
 actions (clic face, ±90°, angles,    │                               │
 orientation automatique, réinit.) ───┘                               ├─► PrintEstimator (poids, durée, supports)
                                                                      ├─► FeasibilityAnalyzer (+ volume du plateau)
 plateau (imprimante, saisie) ───────────────────────────────────────►├─► découpe Bambu (STL temporaire si tourné)
                                                                      └─► Enregistrer le modèle orienté
```

Une action d'orientation : (1) annule les calculs en cours, (2) recalcule `_mesh`, (3) vide mesures, coupe, comparaison et marqueurs,
(4) recharge la vue (`SetMesh`), (5) relance l'estimation et la faisabilité (délai de 250 ms comme aujourd'hui) et la découpe Bambu.
Le volume (`_measures`) n'est pas recalculé : une rotation ne le change pas.

## 6. Interface

Onglet **Outils** : groupe « Orientation » avec les boutons X+/X−/Y+/Y−/Z+/Z−, trois champs d'angles, « Poser sur une face »,
« Orientation automatique », « Réinitialiser », « Enregistrer le modèle orienté ». Onglet **Infos** : sélecteur « Plateau » sous
« Vitesse de la machine », et une ligne « Plateau 256 × 256 × 256 mm — la pièce tient / dépasse de … ». Touche O : orientation
automatique. Les actions sont désactivées pendant la comparaison de deux versions (la comparaison est effacée si l'on tourne).

## 7. Gestion des cas limites

- **Plateau illisible** : retour au plateau générique, message d'information, pas d'erreur.
- **Pièce plus grande que le plateau** dans toutes les orientations : contour rouge, faisabilité « dépasse le volume d'impression », l'orientation
  automatique le dit et propose la moins mauvaise.
- **Maillage non étanche** : l'orientation manuelle fonctionne ; l'orientation automatique n'évalue que ce que le moteur sait mesurer (les
  supports et la base se calculent sur les normales) et signale que le résultat est peu fiable.
- **Très gros modèle** (> 1 million de triangles) : la rotation se fait, l'orientation automatique est limitée à 6 candidats et prévient.
- **Annulation** : changer de modèle ou relancer une action annule l'orientation automatique en cours.

## 8. Tests

Automatiques (xUnit) :
- `ModelOrientation` : poser sur −Z un cube incliné ; `RotateAxis` quatre fois 90° = identité ; la base est à Z = 0 et le centre en X/Y à 0 ; angles d'Euler aller-retour.
- `PlateSize.Parse` : formats courants, virgule décimale, valeurs non numériques, ordre, plateaux ronds (`null`).
- `AutoOrient` : un T posé sur la tête sort la pièce à plat sans support ; un champignon se retourne cap vers le bas ; un cube ne bouge pas (égalité, rotation identité) ; candidat qui déborde écarté ; annulation.
- `StlWriter` : relecture par `MeshLoader`, mêmes sommets, échelle en mm.
- Découpe : l'argument `--scale` n'est pas passé pour un STL temporaire en mm (test de `BuildArguments` existant étendu).

Manuels (vérifiés à l'écran, captures) : plateau dans la grille infinie, contour rouge au dépassement, outil « poser sur une face », orientation automatique sur le crâne.

## 9. Ordre de réalisation

1. `PlateSize` et `ModelOrientation` (purs) avec leurs tests.
2. Branchement dans le visualiseur : `_original`/`_mesh`, boutons, champs d'angles, réinitialisation, recalcul des analyses, faisabilité avec le plateau.
3. Plateau dessiné dans le shader, sélecteur d'imprimante et saisie.
4. Outil « Poser sur une face ».
5. `AutoOrient` dans le moteur, bouton et rapport du gain.
6. `StlWriter`, découpe Bambu sur la pièce tournée, « Enregistrer le modèle orienté ».
7. Journal des modifications, documentation, livraison.

## 10. Risques

- **Coût de l'orientation automatique** sur de gros maillages : 18 candidats × 15 à 80 ms ; limité à 6 candidats au-delà d'un million de triangles, asynchrone, annulable.
- **Qualité de la note** : la matière de supports du moteur est une estimation (rapports estimé/réel de 0,2 à 1) ; l'ordre entre deux orientations est plus fiable que la valeur absolue, c'est ce que l'on compare.
- **Changement de repère** : mesures et coupes sont dans le repère de la pièce posée ; elles sont effacées à chaque rotation pour ne jamais mélanger deux repères.
- **Mémoire** : une copie de plus des positions (≈ 2,4 Mo pour 200 000 triangles) ; négligeable.
