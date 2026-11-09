# Refonte de l’interface — plan

État au 9 novembre 2026 : Avalonia 12.1.4, thème Fluent, interface sombre.
64 fichiers `MainWindow*.cs`, 46 fenêtres de dialogue, 4 feuilles de style
(`Theme`, `Brand`, `Controls`, `Workspace`). La plupart des écrans sont construits
en C# avec des tailles, marges et couleurs écrites au cas par cas (≈ 120 `FontSize`).

Objectif : une interface moderne où **tout s’aligne sur une même grille** et où
chaque page de même nature a la même structure. Les données, la synchronisation et
le serveur ne changent pas.

## Direction visuelle

Deux pistes, maquettées sur la page Projets :

- **A · Bleu nuit épuré** — garde l’identité actuelle (fond `#0C1A2D`, action
  `#245CFF`, accent cyan) ; moins de bordures et de cadres, plus d’espace.
- **B · Graphite moderne** — gris neutre (`#141518`), une seule couleur d’action
  (`#5B7CFA`), statuts en pastille de couleur sur le texte, presque aucun fond de
  carte.

À choisir avant l’étape 1. Un thème clair pourra s’ajouter ensuite à l’une ou
l’autre : il ne demandera que de nouvelles valeurs pour les mêmes jetons.

## Règles communes (étape 1)

Définies une seule fois dans `Styles/Tokens.axaml` et une classe `Ui` en C#.

| Jeton | Valeurs |
|---|---|
| Espacement | 4 · 8 · 12 · 16 · 24 · 32 · 48 — aucune autre marge ni aucun autre écart |
| Texte | 12 (légende) · 13 (secondaire) · 14 (courant) · 16 (titre de section) · 20 (titre de page) · 28 (chiffre clé) |
| Graisse | normale · semi-grasse — pas d’autre |
| Arrondis | 6 (contrôles) · 10 (cartes) · 999 (pastilles) |
| Hauteurs | 36 (contrôle compact) · 40 (contrôle) · 48 (ligne de tableau) |
| Surfaces | fond · surface · surface relevée · survol · sélection |
| Texte | principal · secondaire · discret · exemple de champ |
| Rôles | action · succès · attention · danger, chacun en plein et en doux |

Un test lit les fichiers d’interface et refuse toute taille de police, marge ou
couleur écrite en dur hors des jetons (liste d’exceptions explicite et courte).

## Éléments réutilisables (étape 2)

- `PageHeader` : titre, sous-titre, zone d’action à droite (un seul bouton principal).
- `ListToolbar` : recherche (largeur fixe) · puces de filtre · menu « Plus de filtres »
  · à droite compteur et bouton « Nouveau … ». Passe à la ligne proprement.
- `DataTable` : définition des colonnes une fois, partagée par l’en-tête et les
  lignes ; montants alignés à droite ; menu « ⋯ » toujours dans la dernière colonne ;
  pagination identique en haut et en bas.
- `Section` : titre, description, contenu, action facultative ; repliable ou non.
- `Field` : libellé, champ, aide, erreur — remplace `FormKit.Field`.
- `EmptyState`, `StatusPill`, `Badge`, `KeyValueRow` : une seule version de chacun.

Chaque élément a son test de structure et une capture à 960, 1280 et 1600 px.

## Pages (étapes 3 à 6)

Ordre choisi pour livrer tôt ce qui sert le plus, une étape = une livraison :

3. **Coque** : menu de gauche, en-tête de page, recherche globale, notifications.
4. **Listes** : Projets, Clients, Filaments, Imprimantes, Collections, Corbeille,
   Fichiers — toutes sur `ListToolbar` + `DataTable`.
5. **Fiches et formulaires** : fiche projet, fiche client, panneaux latéraux et
   les 46 fenêtres de dialogue sur `Field` / `Section`.
6. **Pages métier** : Accueil, Tâches, Estimations, Recettes, Achats, Paramètres.

## Vérification à chaque étape

- Tous les tests existants passent.
- Captures des pages touchées aux trois largeurs, relues avant livraison.
- Test d’alignement : dans une page, les bords gauches des blocs et les colonnes
  d’en-tête et de lignes tombent sur la grille de 4 px et coïncident.
- Livraison habituelle : exe Windows remplacé (empreinte vérifiée) et archive Linux.

## Hors périmètre

Logique métier, stockage, synchronisation, serveur, site web, application Android.
