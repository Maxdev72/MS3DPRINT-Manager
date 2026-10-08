# Migration Avalonia et modernisation de l’interface

## Objectif

Remplacer l’interface WPF de MS3DPRINT Manager par une interface Avalonia
moderne, tout en conservant les données locales et toutes les fonctions de la
version Windows actuelle. La première livraison Avalonia cible Windows x64.
L’architecture doit permettre de publier ensuite l’application pour macOS et
Linux sans réécrire les règles métier ou les écrans.

## Décisions validées

- Le socle cible est .NET 10.
- Avalonia remplace WPF pour l’application.
- Windows est la plateforme de référence et la seule plateforme livrée dans
  cette migration.
- L’interface adopte une console immersive moderne : thème sombre exclusif,
  navigation latérale compacte, surface bleu-noir, accent bleu, panneaux
  lisibles et aperçu contextuel.
- L’accueil est une vue d’ensemble de l’atelier : indicateurs, activité
  récente et création rapide.
- La fiche projet présente les fichiers dans le panneau principal et un aperçu
  contextuel à droite. Les données et documents du projet restent accessibles
  sans changer d’écran.
- La livraison Avalonia ne remplace l’exécutable actuel qu’après validation de
  la parité fonctionnelle.

## Architecture cible

La solution comprend trois frontières explicites.

1. `MS3DPRINT.Manager.Core` cible `net10.0`. Il contient les entités, règles
   métier, accès à l’espace géré, recherche, corbeille, fiches et opérations
   sur les fichiers. Il ne référence aucune API WPF, Avalonia ou Windows.
2. `MS3DPRINT.Manager.Desktop` cible `net10.0`. C’est l’application Avalonia,
   ses vues `.axaml`, ses modèles de vue, les styles et les composants de
   navigation.
3. `MS3DPRINT.Manager.Windows` cible `net10.0-windows`. Cette couche fournit
   les adaptateurs Windows : ouverture de fichiers via le système, informations
   matérielles et intégration spécifique au système. L’application Avalonia
   référence cette couche uniquement au démarrage de la cible Windows.

Pendant la migration, `MS3DPRINT.Manager.App` reste l’application WPF de
référence. Elle n’est supprimée qu’après les tests de non-régression et la
livraison Avalonia. Les deux applications utilisent le même projet Core et le
même format de données, mais ne s’exécutent pas simultanément sur le même
dossier utilisateur durant la recette.

## Données et compatibilité

Le format existant demeure inchangé : arborescence de travail, dossier `data`,
métadonnées `.ms3dprint-manager`, registre des codes clients et documents
utilisateur. Aucune migration de données n’est exécutée au premier lancement.

Les services Core continuent de refuser les déplacements ou suppressions qui
sortent de l’espace géré et de ne jamais écraser un élément existant. Toute
erreur de fichier apparaît dans une notification non bloquante lorsque
l’opération peut être reprise ; les actions irréversibles conservent une boîte
de confirmation explicite.

## Expérience Avalonia

L’application comporte une seule fenêtre principale avec :

- une barre latérale pour Accueil, Projets, Clients, Collections, Filaments,
  Corbeille et Paramètres ;
- une barre supérieure avec fil d’Ariane et recherche universelle ;
- une zone de contenu pilotée par la navigation ;
- un panneau contextuel qui affiche l’aperçu ou les détails de l’élément
  sélectionné ;
- une palette de commandes accessible avec `Ctrl+K` pour la recherche, la
  création et les actions fréquentes.

Le thème sombre est obligatoire. Les jetons de style centralisent les couleurs,
espacements, rayons, typographies et états clavier. Les contrastes de texte,
les libellés des icônes, l’ordre de tabulation et le focus visible font partie
de chaque contrôle. La fenêtre s’adapte aux tailles usuelles de bureau ; à
partir d’une largeur insuffisante, le panneau contextuel se masque et devient
une vue ouvrable à la demande.

## Fonctions reprises

La version Avalonia Windows couvre l’ensemble des fonctions actuelles :

- gestion des clients, projets, collections, fournisseurs, modèles, produits
  et filaments ;
- création, renommage, déplacement, importation, ouverture et suppression des
  dossiers et documents gérés ;
- corbeille et restauration ;
- recherche à la demande sur noms, codes, références et fichiers ;
- thèmes de données existants, préférences et suggestions d’entreprises ;
- aperçu des images, des PDF, des modèles STL et OBJ ;
- détection matérielle et préférences Windows.

Les opérations métier sont déclenchées depuis les modèles de vue et exécutées
par Core. Une opération longue (import, recherche, lecture d’un gros modèle)
affiche une progression ou un état d’attente et laisse la fenêtre réactive.

## Prévisualisation des fichiers

Les aperçus sont isolés derrière des interfaces du projet Desktop :
`IImagePreviewService`, `IPdfPreviewService` et `IModelPreviewService`. Les
vues ne dépendent pas des bibliothèques de rendu.

- Les images PNG, JPEG, BMP, GIF et TIFF utilisent les décodages Avalonia.
- Les PDF utilisent `Avalonia.Controls.PdfViewer` et le moteur PDFium Windows.
  Ce contrôle fournit un rendu natif extensible à macOS et Linux lorsque ces
  cibles seront ajoutées. Le fichier de notices tierces inclura la licence BSD
  3-Clause de PDFium.
- Les STL et OBJ utilisent `Ab4d.SharpEngine.AvaloniaUI` et
  `Ab4d.SharpEngine.Assimp`, avec un adaptateur qui conserve les opérations
  existantes : orbitation, zoom, cadrage, informations de dimensions et test
  facultatif de parois. SharpEngine est retenu car il prend en charge Avalonia
  et les trois systèmes de bureau. Une licence commerciale valide est requise
  avant la livraison finale ; la phase de migration emploie la licence d’essai
  du fournisseur uniquement pour la validation technique.

Si un aperçu ne peut pas être chargé, l’application affiche le nom du fichier,
la cause lisible de l’échec et un bouton pour l’ouvrir avec l’application
Windows associée. Les fichiers non prévisualisables suivent directement ce
même chemin.

## Publication Windows

La nouvelle application est publiée en autonome pour `win-x64`. Le binaire
interne peut avoir son nom de projet ; le livrable utilisateur est toujours
copié dans `sources/` sous le seul nom `MS3DPRINT Manager.exe`. La livraison
remplace le fichier précédent, conserve `data`, `.ms3dprint-manager` et les
documents, et vérifie que les empreintes SHA256 du binaire publié et du binaire
livré sont identiques. Une instance déjà ouverte n’est jamais arrêtée de force.

## Validation

La migration est acceptée lorsque :

1. les tests Core passent sous .NET 10 ;
2. l’application Avalonia Windows construit et démarre en autonome ;
3. chaque fonction reprise est exercée sur une copie de données de recette,
   notamment import, déplacement, corbeille, restauration et recherche ;
4. images, PDF, STL et OBJ s’affichent, et un échec d’aperçu propose une sortie
   sûre ;
5. navigation clavier, focus, contrastes et mise à l’échelle 125 % et 150 %
   sont vérifiés ;
6. les données créées avec WPF restent lisibles dans Avalonia et inversement ;
7. la publication Windows respecte le nom de livraison et l’égalité SHA256.

## Découpage d’implémentation

1. Mettre à niveau Core et ses tests vers .NET 10.
2. Créer le projet Avalonia, le thème sombre et le squelette de navigation.
3. Migrer accueil, recherche et écrans de listes.
4. Migrer les fiches, fenêtres de création et opérations de fichiers.
5. Intégrer les aperçus images, PDF et 3D.
6. Ajouter les adaptateurs Windows, les tests de parité et la publication.
7. Exécuter la recette complète avant de remplacer le livrable WPF.
