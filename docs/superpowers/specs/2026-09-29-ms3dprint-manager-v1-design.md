# MS3DPRINT Manager — conception V1

## Objectif

Créer une application Windows locale et portable qui structure le stockage
Nextcloud de MS3DPRINT sans supprimer ni remplacer de contenu existant.

## Périmètre V1

L'application gère l'initialisation de l'arborescence principale, la création
de clients, de projets clients, de modèles 3D, de produits MS3DPRINT et de
fournisseurs. Elle permet également d'ouvrir le dossier racine dans
l'Explorateur Windows.

Le répertoire de stockage géré est :

`C:\\MS3DPRINT\\Nextcloud\\MS3DPRINT`

## Distribution et architecture

L'application est développée en .NET 8 avec WPF et publiée comme un unique
exécutable portable et autonome. Elle ne requiert ni installation, ni service
externe, ni dépendance payante.

Le code est séparé en trois responsabilités :

- logique métier : normalisation des noms, références projet et règles de
  numérotation ;
- accès au système de fichiers : vérification et création sûre des dossiers ;
- interface WPF : formulaires, retours utilisateur et ouverture de dossiers.

La configuration locale de l'application contient les codes clients et les
préférences éventuelles. Les dossiers existants restent la source de vérité
pour les références et ne sont jamais écrasés.

## Arborescences

### Racine

`01_CLIENTS`, `02_MODELES_3D`, `03_PRODUITS_MS3DPRINT`,
`04_COMMUNICATION`, `05_MACHINES_MATERIAUX`, `06_FOURNISSEURS`,
`07_ADMINISTRATIF`, `08_COMPTABILITE`, `09_COMMERCIAL`,
`10_RESSOURCES_MS3DPRINT`, `99_ARCHIVES`.

### Client et projet

Un client est créé dans `01_CLIENTS/NOM_NORMALISE` avec `00_CLIENT` et
`99_ARCHIVES`.

Un projet est créé sous son client au format
`CODECLIENT-AAAA-001_NOM_PROJET`. Son numéro est le maximum existant pour ce
client et cette année, augmenté de un. Il contient :

`00_BRIEF_CLIENT`, `01_DEVIS_FACTURES`, `02_FICHIERS_CLIENT`,
`03_CAO_3D/01_MASTER`, `03_CAO_3D/02_STEP`, `03_CAO_3D/03_STL`,
`04_IMPRESSION_3D/01_STL_PRODUCTION`, `04_IMPRESSION_3D/02_3MF`,
`04_IMPRESSION_3D/03_PARAMETRES`, `05_PRODUCTION`, `06_PHOTOS_RENDUS`,
`07_LIVRAISON`, `99_ARCHIVES`.

### Modèle 3D

Un modèle est créé dans `02_MODELES_3D/NOM_NORMALISE` avec :

`01_REFERENCES`, `02_SOURCE`, `03_CAO_MASTER`, `04_STEP`, `05_STL`, `06_3MF`,
`07_RENDUS`, `08_PHOTOS`, `99_ARCHIVES`.

### Produit MS3DPRINT

Un produit est créé dans `03_PRODUITS_MS3DPRINT/NOM_NORMALISE` avec :

`01_CONCEPT_REFERENCES`, `02_CAO`, `03_STL`, `04_3MF`,
`05_TESTS_PROTOTYPES`, `06_PHOTOS_RENDUS`, `07_MARKETING`, `08_PRIX_COUTS`,
`99_ARCHIVES`.

### Fournisseur

Un fournisseur est créé dans `06_FOURNISSEURS/NOM_NORMALISE` avec :

`01_CONTACT`, `02_TARIFS`, `03_COMMANDES`, `04_FACTURES`,
`05_DOCUMENTATION`, `99_ARCHIVES`.

## Interface

La fenêtre principale affiche le nom de l'outil, le chemin de stockage, un
bouton de vérification de l'arborescence et les actions de création. Chaque
action ouvre un formulaire dédié.

Le formulaire client affiche le nom normalisé et suggère un code client,
modifiable avant création. Le formulaire projet propose un client existant,
le code mémorisé, l'année, le nom du projet et un aperçu de la référence.
Après création, le récapitulatif offre l'ouverture du dossier créé.

## Sécurité et erreurs

La normalisation convertit les noms en majuscules ASCII, retire les accents et
caractères spéciaux, et remplace les séparateurs par un underscore unique.

Avant toute création, l'application valide les champs et vérifie le conflit
de chemin. Un conflit ou une erreur de disque est signalé clairement ; aucune
suppression, écriture de remplacement ou création partielle n'est effectuée.
La vérification de l'arborescence racine ajoute uniquement les dossiers
manquants.

## Vérification

Des tests automatisés couvriront la normalisation, la proposition de code,
la référence projet, la numérotation et les plans d'arborescence. Une
vérification manuelle couvrira le lancement de l'exécutable portable, les
formulaires, les conflits et l'ouverture de l'Explorateur Windows.

## Hors périmètre V1

Les fiches client et projet complètes, les statuts, la recherche, l'historique,
les devis/factures et l'automatisation documentaire restent prévus pour des
versions ultérieures.
