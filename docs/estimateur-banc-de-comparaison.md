# Estimateur MS3DPRINT : banc de comparaison avec Bambu Studio

Date : 16 octobre 2026. Le moteur est dans `src/MS3DPRINT.Estimator`, le banc dans `tools/EstimatorBench`.

## Ce que fait le moteur

Il estime le poids, la longueur de filament et le temps d'impression d'un maillage **sans lancer de découpeur**, à partir de
quelques paramètres : hauteur de couche, nombre de parois, remplissage, épaisseur des dessus/dessous, supports.

1. Le maillage est coupé en couches (au milieu de chaque couche). Les segments de coupe sont chaînés en contours fermés.
2. Chaque contour extérieur forme un îlot qui reprend les trous qu'il contient. Les parois sont limitées à la matière de l'îlot.
3. L'intérieur est séparé en surfaces pleines (dessus/dessous, vues d'après la différence d'aire avec les couches voisines) et en remplissage clairsemé.
4. Le temps vient du volume de chaque type de trait, de la courbure totale des parois (freinage dans les coins), des surplombs et du nombre de couches.
5. Les supports sont estimés en lançant un rayon vers le bas sous chaque face en surplomb.

Aucun code ni profil d'un découpeur n'est utilisé : seules des mesures de référence ont servi à caler les coefficients.

## Un moteur moyen, pas le profil d'une imprimante

Le moteur ne dépend d'aucune imprimante en particulier. Ses constantes ont été calées sur la **moyenne de cinq imprimantes**
mesurées avec Bambu Studio (A1 mini, A1, P1S, X1 Carbon, H2S), avec leurs vraies qualités de couche (0,12 à 0,28 mm), deux
matières (PLA, PETG), des remplissages de 10 à 50 %, 2 à 4 parois, avec et sans supports : 450 découpes, 15 pièces (10 formes
simples, 5 pièces réelles de l'atelier). Bambu Studio ne sert que d'instrument de mesure : aucun de ses codes ni profils n'est utilisé.

Ce qui fait varier le résultat d'un cas à l'autre est ramené à trois entrées, qui ne sont pas un profil d'imprimante :

| Entrée | Rôle |
|---|---|
| Matériau : densité, **débit maximal** (mm³/s) | le poids ; et la durée : le PETG (14), l'ABS (16) ou le TPU (3,6) fondent moins vite que le PLA (20) |
| Hauteur de couche, parois, remplissage, supports | la géométrie imprimée |
| **Vitesse de la machine** (`HeadSpeedFactor`), 1 par défaut | la rapidité réelle de la tête : moyenne 1, CoreXY rapide 1,35, machine bridée 0,6 |

### Précision mesurée

| | volume (poids) | durée |
|---|---|---|
| Moteur moyen, sans réglage de vitesse | **3 %** d'écart absolu médian | environ **25 %** : surestimée de 24 à 29 % sur P1S, X1C et H2S, sous-estimée de 27 à 34 % sur A1 en PETG |
| Avec la vitesse de la machine (rapide ×1,35 ou lente ×0,6) | 3 % | **6,5 %** sur les cinq machines et les deux matières |

Le poids ne dépend ni de la machine ni du matériau (à la densité près) : ±3 à 4 % partout. La durée dépend de la machine, parce que
les profils de Bambu Studio la brident très différemment (A1 : parois à 60 mm/s et 500 mm/s² contre 200 mm/s et 5 000 mm/s² pour
la P1S). Une moyenne ne peut pas effacer cet écart : elle le répartit des deux côtés, d'où le réglage facultatif.

Le profil Bambu « PLA Basic » de l'A1 déclare un débit maximal de 2 mm³/s : le PLA y est donc limité par le débit et la durée est
très longue, ce qui rend l'estimation facile pour cette combinaison. Les rapports ci-dessus tiennent compte des débits déclarés.

### Limites connues

- **Seules des machines Bambu Lab ont été mesurées** (cinq modèles). Prusa, Creality, Voron… ne sont pas dans la moyenne.
  Le réglage de vitesse (0,6 à 1,35) couvre l'écart observé ; hors de cette plage, il faut le valider.
- **Pièces à coque fine et allongée** (`reel-3`, un fourreau de 190 mm) : volume jusqu'à 25 % trop bas, durée jusqu'à deux fois trop
  courte. Cause non identifiée.
- **Supports** : les rapports estimé/réel varient de 0,2 à 1 selon les pièces (supports en arbre de Bambu, plus généreux que des
  colonnes). La densité de support est réglée à 0,45, à la médiane des 45 cas mesurés : les surplombs plats sont alors surestimés
  d'un facteur 2 à 3 et les pièces organiques à peu près justes. Mieux vaut un devis un peu haut qu'un devis trop bas.
- Un seul diamètre de buse (0,4 mm) a été mesuré.
- Le banc a trouvé que Bambu Studio refuse les STL binaires de cubes de 40 mm et plus (« input model can not be parsed »),
  sans lien avec l'estimateur : les pièces de test font donc 30 mm au plus.

## Relancer le banc

```
dotnet run --project tools/EstimatorBench -c Release -- compare
```

Les réponses de Bambu sont gardées dans `tools/EstimatorBench/reference-multi.json` : seules les combinaisons manquantes sont
redécoupées. `features.csv` contient les mesures brutes (volumes sans calibration, courbure, surplombs, réglages réels de chaque
machine) pour refaire un ajustement. Variables : `BENCH_INFO=1` (taille, alertes et durée de calcul de chaque pièce),
`BENCH_ANGLE` (seuil de supports), `BENCH_CLASSES=1` (donne à chaque machine sa classe de vitesse au lieu de la moyenne).

## Analyse de faisabilité

`FeasibilityAnalyzer.Analyze` (dans `src/MS3DPRINT.Estimator`) dit si un modèle est imprimable et pourquoi, sans découpeur.
Elle renvoie un verdict (imprimable, avec avertissements, non imprimable) et une liste de constats : code, gravité, message en
français, valeur chiffrée et quelques points du modèle à surligner.

| Contrôle | Méthode | Gravité |
|---|---|---|
| Étanchéité | soudure des sommets à 0,1 µm, arêtes partagées par une seule face | Erreur (avertissement si `RequireWatertight = false`) |
| Géométrie non valide | arêtes partagées par plus de deux faces | Erreur |
| Faces retournées | arêtes parcourues dans le même sens par leurs deux faces | Avertissement |
| Maillage entièrement à l'envers | volume négatif | Info (corrigé automatiquement) |
| Pièces séparées | surfaces fermées distinctes ; une cavité interne n'en est pas une | Avertissement |
| Parois fines | rayon vers l'intérieur depuis 30 000 faces, distance à la surface opposée | Erreur si au moins 3 % de la surface est sous la largeur de buse, sinon avertissement |
| Surplombs | rayon vers le bas sous chaque face penchée de plus de 60° | Avertissement (aire et volume de supports) |
| Base de contact | aire de la première couche, élancement | Avertissement |
| Volume d'impression, unités | dimensions comparées à l'imprimante ; modèle de moins de 2 mm ou plus de 1 m | Erreur / Avertissement |

Essai sur les 15 pièces du banc : 0 à 400 ms par pièce (396 ms pour 207 000 triangles). Les verdicts sont cohérents avec ce que
Bambu Studio a imprimé (sphère et cône posés sur une pointe : base minuscule ; champignon et fourreau : supports), mais il n'y a
pas de vérité terrain chiffrée comme pour le poids et le temps.

Limites : une pièce flottante (non posée sur le plateau) n'est pas détectée ; les auto-intersections non plus ; le plafond d'une
cavité fermée compte comme surplomb ; les ponts ne sont pas distingués des surplombs ; une épaisseur est mesurée perpendiculairement
à la face, donc une lame très effilée est signalée sur toute sa longueur.

## Vitesse

0 à 85 ms par estimation, supports compris, de 12 triangles à 207 000 triangles (premier appel avec compilation à la volée).
