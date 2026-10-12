# Design — MS3DPRINT Manager

> Extrait du code (`src/MS3DPRINT.Manager.Desktop/Styles/Theme.axaml` et `Controls.axaml`).

## Thème
Un seul thème, sombre bleu-nuit. Toutes les couleurs sont des jetons `*Brush` dans `Theme.axaml` ;
ne pas écrire de valeur hexadécimale ailleurs.

| Rôle | Jeton | Valeur |
|---|---|---|
| Fond de page | CanvasBrush | #080C14 |
| Barre latérale | SidebarBrush | #0A101B |
| Surface / surélevée | SurfaceBrush / RaisedSurfaceBrush | #0E1522 / #141D2D |
| Survol / appui | HoverBrush / PressedBrush | #1B2740 / #25375A |
| Bordures | BorderBrush / SubtleBorderBrush | #243247 / #1A2538 |
| Texte / atténué / discret | TextBrush / MutedTextBrush / FaintTextBrush | #EAF0FC / #93A5C2 / #7385A3 |
| Accent | AccentBrush (+ Soft, Pressed, Foreground) | #6B9BFF |
| Succès / alerte / danger | SuccessBrush / WarningBrush / DangerBrush (+ Soft) | #46D6A4 / #F4BC52 / #F57F7F |

Contrastes sur SurfaceBrush : texte 15:1, atténué 7:1, discret 4,6:1.

## Composants
- **Boutons** : `primary-action` (une par écran), `ghost`, `danger`, `icon-action` (36×36 minimum, nom accessible obligatoire), `menu-action`.
- **Surfaces** : `Border.panel` (page à plat), `card` / `card-raised` (jamais imbriquées), badges de statut.
- **Listes** : `table-row` (ligne = bouton), en-têtes triables `sort-head`, menu d’actions « ⋯ ».
- **Icônes** : géométries `Icon*` dessinées, trait 2 sur grille 24, via `Icons.Glyph`. Pas de symboles texte.
- **Dialogues** : `DialogWindow` + `FormKit` (Entrée valide, Échap annule, boutons à droite).
- **Notifications** : toast en bas à droite, fermable, texte qui nomme le problème et la solution.

## Règles
Texte minimum 12 px. Une seule déclaration d’élévation par élément. Le menu actif se signale par un fond, pas par un liseré.
