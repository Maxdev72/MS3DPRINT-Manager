# Classement intelligent des fichiers générés par les logiciels externes — conception

Date : 25 octobre 2026. Statut : choix validés en chat (Bambu Studio d’abord, détection en continu, propositions dans le dossier du projet).

## Objectif
Quand un logiciel externe enregistre un projet à côté d’un fichier du projet (cas type : Bambu Studio enregistre un `.3mf` près du STL ouvert), MS3DPRINT le repère, reconnaît son origine, et propose de le classer dans le dossier prévu par le modèle de projet. Rien ne bouge sans confirmation.

## Décisions
- Reconnaissance par le contenu, pas par l’extension : un `.3mf` est un ZIP ; `3D/3dmodel.model` porte l’application (`BambuStudio-…`, `OrcaSlicer-…`) et `Metadata/model_settings.config` les noms des modèles d’origine (`piece.stl`).
- Un `.3mf` d’une autre origine (export CAO par exemple) n’est jamais proposé.
- Destination : dossier de la catégorie `3MF` du modèle de projet (par défaut `04_IMPRESSION_3D/02_3MF`), nom conforme à la convention `nom__RÉFÉRENCE.3mf` ; si le nom existe déjà, un suffixe ` (2)` est ajouté. Aucun écrasement.
- Association : le nom d’un modèle du `.3mf` est comparé aux STL du projet (référence retirée) ; le STL trouvé est affiché comme fichier source.
- Protection : un fichier modifié depuis moins de 15 secondes, verrouillé par un autre programme, illisible ou dans un dossier de sauvegarde/archives n’est pas proposé.
- Détection : analyse du dossier du projet à son ouverture puis toutes les 10 secondes tant que la fiche du projet est affichée (les événements de fichiers sont peu fiables dans un dossier synchronisé).
- Choix « Ignorer » mémorisé dans `.ms3dprint-manager/classification-ignored.json` (chemins relatifs à l’espace de travail).
- Le déplacement réutilise `ProjectFileTransferService` (contrôles de liens, pas d’écrasement).

## Composants (Core)
- `ThreeMfInspector` : lit un `.3mf` (lecture bornée) → `ThreeMfInspection(Slicer, ApplicationVersion, ModelNames)` ou null.
- `ExternalFileClassifier.Scan` : parcourt le projet et produit des `ClassificationProposal`.
- `IgnoredClassificationStore` : mémorise les propositions ignorées.

## Interface (Desktop)
Section « Fichiers à classer » dans le suivi du projet : une ligne par proposition (nom, origine, fichier source, dossier cible) avec « Classer » et « Ignorer ». La section n’apparaît que s’il y a des propositions.

## Hors périmètre
Autres logiciels (PrusaSlicer, Cura), fichiers autres que `.3mf`, surveillance hors fiche projet.
