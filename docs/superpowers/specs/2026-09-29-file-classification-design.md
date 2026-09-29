# MS3DPRINT Manager — classement sécurisé des fichiers

## Objectif

Permettre de déplacer un fichier téléchargé ou reçu vers un projet client
existant, avec un nom qui conserve l'identifiant original du fournisseur tout
en ajoutant la référence MS3DPRINT du projet.

## Parcours utilisateur

Une nouvelle action `Classer un fichier` est ajoutée au tableau de bord.

1. L'utilisateur sélectionne un fichier depuis Téléchargements ou tout autre
   emplacement local.
2. Il choisit un projet client existant.
3. L'application propose un dossier de destination selon l'extension et un
   nom final.
4. Un récapitulatif affiche le fichier source, le projet, le dossier final et
   le nom final. L'utilisateur confirme explicitement le déplacement.
5. Le fichier est déplacé une seule fois vers le dossier du projet ; aucune
   copie n'est laissée dans Téléchargements.

## Nom final

Le nom source est conservé en entier et la référence projet est ajoutée avant
l'extension :

`NOM_ORIGINAL__REFERENCE_PROJET.ext`

Exemples :

- `DEV2026-05__MPO-2026-001.pdf`
- `MPO-STEP-25-GRADE-A__MPO-2026-001.step`

La référence projet ne comprend pas le libellé projet : seule sa partie
`CODECLIENT-AAAA-XXX` est ajoutée. Cela garde les noms compacts et évite de
dupliquer le libellé dans chaque fichier.

## Classement proposé

Le dossier proposé est modifiable par l'utilisateur avant confirmation :

- `.pdf` : `01_DEVIS_FACTURES`
- `.step`, `.stp` : `03_CAO_3D/02_STEP`
- `.stl` : `03_CAO_3D/03_STL`
- `.3mf` : `04_IMPRESSION_3D/02_3MF`
- toute autre extension : `02_FICHIERS_CLIENT`

L'outil conserve l'extension, y compris sa casse si elle est présente dans le
nom source.

## Sécurité

- Le déplacement n'est exécuté qu'après confirmation explicite.
- Le fichier source doit exister et être un fichier ordinaire.
- Le projet sélectionné et le sous-dossier final doivent exister sous
  `01_CLIENTS`.
- Si le chemin de destination existe déjà, l'opération est bloquée : aucun
  fichier n'est écrasé ni remplacé.
- Le fichier source et le dossier final sont affichés dans le récapitulatif.
- Une erreur de disque, d'accès ou de synchronisation laisse le fichier source
  en place lorsque le déplacement n'a pas pu aboutir ; l'application affiche
  le message d'erreur existant adapté au contexte.

## Architecture

La logique pure est placée dans `MS3DPRINT.Manager.Core` : génération du nom,
choix du sous-dossier et validation des chemins. Un service de transfert
effectue le contrôle de conflit puis `File.Move` sans écrasement.

L'interface WPF ajoute un formulaire dédié : choix du fichier avec la boîte de
dialogue Windows, choix du projet et du dossier, aperçu, puis confirmation.
Les opérations de lecture et de déplacement sont lancées hors du thread visuel
afin que l'interface reste réactive avec Nextcloud.

## Hors périmètre

- Création de copies ou conservation d'une seconde version du fichier.
- Écrasement ou fusion de fichiers existants.
- Renommage massif de plusieurs fichiers.
- Analyse du contenu d'un PDF, devis ou facture.
