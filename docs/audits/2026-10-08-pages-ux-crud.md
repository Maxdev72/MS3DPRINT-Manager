# Audit fonctionnel des pages et de leur ergonomie

Version inspectée : commit 31209dc, 8 octobre 2026. Lecture des vues WPF, événements, navigation et services liés, croisée avec les trois captures fournies. Cet audit ne représente pas une nouvelle campagne interactive dans l’application et ne modifie aucun document métier.

## Actions disponibles par page

« Fiche » signifie que l’action se trouve après ouverture de la fiche. « Bloqué sans fiche » désigne les dossiers historiques visibles dans les listes mais redirigés vers un formulaire de complétion.

| Page | Ajout | Modification / renommage / déplacement | Suppression | Consultation et ergonomie |
|---|---|---|---|---|
| Accueil | Clients, projets, modèles, produits, fournisseurs via formulaires | Navigation vers les modules | Via modules | Grandes cartes ; conserver les raccourcis, réserver les tableaux aux listes de données |
| Clients | Nouveau client | Fiche éditable, dossier renommable/déplaçable | Corbeille depuis fiche | Dossier historique bloqué par complétion ; grandes lignes sans colonnes |
| Fiche client | Nouveau projet, import fichiers et nouveau sous-dossier | Champs de fiche, opérations dossier et fichiers | Client entier et fichiers vers corbeille | Formulaire long ; projets et fichiers placés après les champs ; navigateur client ouvre au double clic |
| Projets | Nouveau projet suivi, client complet requis | Fiche, statut, notes, échéance ; dossier renommable/déplaçable | Corbeille depuis fiche | Projet historique bloqué par formulaire ; sélection ouvre immédiatement ; lignes très hautes |
| Fiche projet | Import, sous-dossier, classement de fichier | Champs métier et opérations fichiers | Projet ou fichiers vers corbeille | Non accessible sans fiche ; fichiers après tout le formulaire ; sélection ouvre immédiatement dossier/PDF |
| Modèles 3D | Nouveau dossier puis fiche | Fiche et dossier depuis détail | Corbeille depuis détail | Vue Collection partagée ; grandes lignes, actions après ouverture |
| Produits | Nouveau dossier puis fiche | Fiche et dossier depuis détail | Corbeille depuis détail | Même vue Collection ; mêmes problèmes de densité |
| Fournisseurs | Nouveau dossier puis fiche | Coordonnées et dossier depuis détail | Corbeille depuis détail | Même vue Collection ; création en deux fenêtres successives |
| Détail modèle / produit / fournisseur | Import et sous-dossier | Modifier fiche, renommer/déplacer dossier et fichiers | Corbeille | Dossiers sans fiche consultables, contrairement aux clients/projets ; clic de sélection ouvre les fichiers |
| Filaments | Ajouter, dupliquer | Modifier la sélection | Corbeille | CRUD visible ; présentation multiligne à convertir en colonnes ; prix affiché à deux décimales même si précision stockée supérieure |
| Recherche | Sans objet | Ouvre la fiche ou le fichier trouvé | Sans objet | Lecture/recherche ; résultats multiligne, distinguer sélection et ouverture |
| Corbeille | Alimentée par suppressions | Restauration de la sélection | Purge définitive non proposée | Conforme au choix de restauration ; tableau nom/date plus lisible |
| Classement fichier | Déplace un fichier vers un projet | Choix du nom final et destination | Sans objet | Le déplacement est explicitement indiqué ; séparer visuellement de l’import, qui copie |
| Aperçu PDF / image | Sans objet | Consultation et changement de page PDF | Sans objet | Fenêtre redimensionnable et maximisable par Windows ; pas de bouton Agrandir, zoom ni défilement d’une page zoomée |
| Aperçu 3D | Ouverture de modèle | Contrôles de visualisation/analyse | Sans objet | Outil de consultation ; CRUD porté par le navigateur de fichiers |
| Paramètres | Sans objet | Thème et accent ; informations des stockages | Sans objet | Ne pas présenter les cartes de fournisseurs de stockage comme des connexions opérationnelles supplémentaires |

## Constats prioritaires

1. **Bloquant — accès aux projets sans fiche.** MainWindow.xaml.cs, ShowProjectDetail, redirige systématiquement vers CreateTrackedProjectWindow lorsque Profile est null. Le formulaire ne propose que les clients avec fiche complète. C’est exactement la combinaison visible sur les captures : les fichiers existent mais leur consultation depuis Projets est inaccessible. Le constructeur ProjectDetailViewModel exige aussi un profil. Même obstacle côté clients dans ShowClientDetailCore.
2. **Important — densité des listes.** ClientsView et ProjectsView empilent informations, marges et badge dans chaque ligne. CollectionView, FilamentsView, SearchView et TrashView utilisent également des modèles de lignes plutôt que des colonnes. Absence d’un tableau homogène avec tri par en-tête.
3. **Important — consultation et sélection confondues.** Les listes clients/projets et les navigateurs projet/collection ouvrent depuis SelectionChanged, puis effacent la sélection. Le navigateur client utilise déjà le double clic. Cette divergence gêne les actions sur une sélection, le clavier et l’apparition des aperçus.
4. **Important — fichiers relégués en bas.** ProjectDetailView empile statut, échéance, description et notes avant le navigateur. ClientDetailView est également un long formulaire. L’utilisateur doit défiler pour son action fréquente : consulter les documents.
5. **Important — actions peu découvrables.** Les opérations fichiers déplacer/supprimer sont dans le menu contextuel ; renommer dispose d’un bouton qui redemande le fichier dans une boîte Windows, au lieu d’utiliser la sélection courante. Les listes métier ne montrent pas les actions disponibles avant l’ouverture de la fiche.
6. **Amélioration — PDF.** DocumentPreviewWindow affiche une Image ajustée à la fenêtre. Ajouter un bouton explicite Agrandir/Rétablir, puis zoom et défilement pour lire le contenu. Le rendu PDF doit tenir compte du niveau de zoom si une qualité supérieure est nécessaire ; agrandir une image existante ne suffit pas à améliorer sa netteté.

## Refonte proposée à valider

- Tableaux compacts dans Clients, Projets, Modèles, Produits, Fournisseurs, Filaments, Recherche et Corbeille. Ligne de 36 à 40 pixels, en-têtes triables, colonnes redimensionnables, noms tronqués avec infobulle, défilement horizontal si nécessaire et virtualisation. Préserver les quatre thèmes et l’accent choisi.
- Simple clic pour sélectionner ; double clic ou Entrée pour ouvrir. Barre d’actions visible adaptée à la sélection : Ouvrir, Modifier/Compléter, Renommer, Déplacer, Supprimer. Désactiver les actions incompatibles avec une explication ; conserver la corbeille et les protections métier.
- Détail client/projet accessible même sans fiche. Onglets Fichiers, Informations et, pour un client, Projets. Fichiers visible en premier. Avertissement discret « Fiche à compléter » avec action explicite, sans imposer de formulaire pour consulter les documents.
- Aucune création automatique de fiche ni données client inventées. Lorsqu’une édition métier exige une fiche client, fournir une action guidée pour la compléter puis reprendre la complétion du projet. Renommage/déplacement métier restent soumis à leurs règles d’identité.
- Tableaux de fichiers : Nom, Type, Taille lisible, Modifié ; chemin courant, Remonter et actions sur la sélection. Les importations et sous-dossiers restent accessibles dans les dossiers historiques, avec les contrôles de chemin existants.
- Aperçu PDF : Agrandir/Rétablir conserve la page courante ; zoom moins/plus, pourcentage et Ajuster à la largeur, avec défilement. Préserver navigation entre pages et gestion des erreurs de rendu.

## Vérification attendue pour l’implémentation

Tests de navigation projet/client sans fiche sans écriture implicite ; tests de complétion guidée et retour au projet ; sélection sans ouverture, ouverture double clic/Entrée ; disponibilité des actions selon la sélection ; protections corbeille/liens inchangées ; agrandissement PDF conservant page et retour à taille normale. Vérifications visuelles des tableaux et détails dans les quatre thèmes, fenêtre étroite et écran standard. Suite complète puis publication et remplacement du seul MS3DPRINT Manager.exe, avec contrôle SHA256.

## Mise en œuvre — 8 octobre 2026

La refonte a été appliquée : les listes concernées sont des tableaux compacts triables, avec ouverture explicite et barre d’actions sur la sélection. Les dossiers historiques s’ouvrent sans créer de fiche ; leurs fichiers restent consultables et la complétion client puis projet est déclenchée uniquement par l’action dédiée. Les détails affichent les fichiers avant les informations métier.

Le navigateur de fichiers affiche Nom, Type, Taille et Modifié. Les actions ouvrir, renommer, déplacer et supprimer utilisent la ligne sélectionnée, tout en conservant la corbeille et les protections d’identité des dossiers sans fiche. L’aperçu PDF offre Agrandir/Rétablir, zoom, ajustement à la largeur et conservation de la page.

Validation : 428 tests automatisés, captures de toutes les pages dans les quatre thèmes et à largeur réduite, ainsi que 20 captures de l’aperçu PDF. La publication a remplacé le seul exécutable de livraison après contrôle SHA256.

## Parcours guidé — livraison complémentaire, 8 octobre 2026

Les entrées « Nouveau client » et « Nouveau projet » forment désormais un parcours continu : l’assistant projet peut créer puis sélectionner un client, et la création client peut enchaîner directement vers un projet. Après création, le choix entre ouvrir la fiche créée et revenir à l’accueil évite toute importation forcée.

Dans une fiche projet, les fichiers sont présentés en premier avec des onglets compacts, les tailles de dossiers sont calculées sans bloquer l’interface, et les commandes sur une ligne apparaissent seulement après sélection. Le tableau de bord expose les projets récemment modifiés et les fiches incomplètes ou échéances dépassées.

Validation automatisée : 440 tests réussis, aucun test ignoré (`outputs/logs/guided-workflow-full-*.binlog`). Publication Portable Win-x64 créée dans `outputs/MS3DPRINT-Manager`. Le fichier de livraison unique `MS3DPRINT Manager.exe` a été remplacé ; son SHA256 est `F70E16023215E8C7288F6C0A24B562FBDAFC4E264EC3F45E3D2298115F2ED4BC`, identique à celui de l’exécutable publié.
