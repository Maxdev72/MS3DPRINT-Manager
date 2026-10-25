# Correctifs Avalonia, navigation fichiers et mesures

**Objectif approuvé :** appliquer les correctifs Avalonia, faciliter la recherche/réouverture des fichiers, puis mesurer le résultat en conservant l’architecture et le thème.

**Architecture :** framework 12.1.4 (hors WebView, cycle distinct), barre de recherche/famille commune aux dossiers projet et génériques, filtrage de l’inventaire en mémoire et pagination existante de 50 lignes. Historique de 20 chemins relatifs par espace dans les métadonnées, seuls fichiers gérés non exécutables ; six raccourcis à l’accueil avec accès au dossier. Pas de retrait manuel d’historique dans ce lot. Les fichiers absents restent explicitement signalés. La cellule fichier gagne une hiérarchie nom/type/aperçu, avec le cache et les limites existants.

**Tests/mesures :** tests de comportement avant code, mesures headless Release sur dossiers synthétiques de 100/1 000/10 000 entrées. Comparer même protocole avant/après : chargement avec création/mise en page, lignes réalisées et allocations gérées, puis filtrage. Ces mesures ne certifient ni FPS GPU, ni stockage réseau, ni latence matérielle réelle.

**Contraintes :** travail non committé conservé ; données réelles et instance réelle non touchées ; confirmations destructives maintenues ; un seul `MS3DPRINT Manager.exe` livré après tests et publication ; binlogs uniques sur chaque commande .NET. Aucun devis/facture PDF ni suivi de production ajouté.

## Lots
- [x] Enregistrer la mesure initiale et vérifier les parcours existants avec Avalonia 12.1.3.
- [x] Passer toutes références framework/Headless 12.1.3 à 12.1.4 ; vérifier focus, clavier, menus et estimateur.
- [x] `Core/Files/FileCatalogFilter.cs` : mots combinés, accents/casse, familles STL/3MF/OBJ, CAO, PDF, images, autres ; dossiers toujours accessibles avec filtre de format, texte applicable à leur nom.
- [x] `Core/Files/RecentFileStore.cs` : historique borné, écritures atomiques/verrou local, chemins relatifs sûrs, aucun effet sur les fichiers ; tests d’isolation et corruption.
- [x] `Desktop/FileBrowserView.cs` : contrôles réutilisables, filtres sans disque, pagination et état sans résultat ; intégration dans Folders/ProjectFiles, actions existantes.
- [x] `MainWindow.Files.Recent.cs` et accueil : lecture asynchrone protégée, raccourcis réouvrir/voir le dossier, suivi après ouverture effective interne ou transfert à une application.
- [x] Tests UI, captures 960/1600 px, mesures finales même protocole, revue ciblée.
- [x] Documentation, suite complète, publication PortableWinX64 et vérification des empreintes de livraison.

## Sources de décision
[Veille du 25 octobre](../../veille-technologique-2026-10-25.md), [Avalonia 12.1.4](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.4), [performances](https://docs.avaloniaui.net/docs/app-development/performance).
