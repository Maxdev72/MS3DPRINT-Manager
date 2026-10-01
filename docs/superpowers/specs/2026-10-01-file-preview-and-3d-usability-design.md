# Aperçus des fichiers et visualiseur 3D — conception

## Portée approuvée

Le lot validé le 1er octobre 2026 ajoute la consultation dans MS3DPRINT Manager des images courantes (PNG, JPEG, BMP, GIF, TIFF) et des PDF, ainsi que le glisser-déposer et les dimensions dans le visualiseur STL/OBJ. Il ne comprend pas le 3MF.

## Architecture

Une fenêtre WPF commune affiche les images avec le décodeur WPF et les pages PDF avec `Windows.Data.Pdf`, une API fournie par Windows. L’application et ses tests ciblent Windows 10 version 2004 ou supérieure pour référencer cette API sans runtime navigateur. Le chargement et le rendu se font hors du thread UI, sans modifier le fichier source. Les PDF multipages proposent précédent/suivant et un compteur.

La classification des fichiers est centralisée dans Core. Les vues de projet, collection et recherche ouvrent la fenêtre d’aperçu pour les types pris en charge ; les autres fichiers conservent le comportement Windows existant. Les erreurs de lecture sont affichées dans la fenêtre, sans fermeture de l’application.

Le visualiseur 3D accepte un fichier au lancement ou une fenêtre vide ouverte depuis le menu. Un bouton de sélection et le glisser-déposer choisissent un STL/OBJ. Après chargement, la boîte englobante de la géométrie fournit X, Y, Z ; l’interface précise « unités du modèle » car STL/OBJ ne portent pas d’unité fiable.

## Contraintes et vérification

- Aucun fichier utilisateur n’est déplacé, renommé ou écrasé.
- Pas d’indexation ou de rendu permanent en arrière-plan ; seuls les fichiers ouverts sont chargés.
- Pas de dépendance payante ni de runtime WebView2 fixe à distribuer.
- Tests des formats, du comportement multipage PDF, des dimensions et de la sélection 3D ; test complet de la solution et publication portable avant copie de l’exécutable.
