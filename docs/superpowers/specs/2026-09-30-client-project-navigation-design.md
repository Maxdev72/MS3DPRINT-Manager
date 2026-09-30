# MS3DPRINT Manager — navigation Clients et Projets

## Objectif

Faire évoluer MS3DPRINT Manager d'un tableau de bord de création de dossiers
vers une application de gestion locale. L'utilisateur doit pouvoir consulter,
rechercher et organiser ses clients, projets et fichiers depuis le logiciel.
L'Explorateur Windows reste disponible comme solution de secours, mais ne
constitue plus le parcours normal.

La réalisation est volontairement progressive : **Clients**, puis **Projets**,
puis les autres collections métier.

## Espace de travail indépendant du stockage

L'application manipule un espace de travail : un dossier racine local choisi
par l'utilisateur. Nextcloud est le premier fournisseur, mais aucun module
métier ne dépend de Nextcloud ni de son API.

- Aujourd'hui, la racine est `C:\\MS3DPRINT\\Nextcloud\\MS3DPRINT`.
- Demain, la même racine peut être fournie par Google Drive, Dropbox ou un
  dossier local synchronisé par un autre outil.
- Les fournisseurs V1 se limitent à la sélection d'un dossier local déjà
  synchronisé. Il n'y a ni authentification cloud, ni tâche réseau, ni service
  en arrière-plan.
- Un futur connecteur de fournisseur ne devra modifier que la résolution et la
  sélection de l'espace de travail, jamais les pages Clients ou Projets.

Chaque espace de travail possède un dossier technique synchronisé nommé
`.ms3dprint-manager` à sa racine. Il ne contient que les informations de
gestion du logiciel et n'est jamais affiché dans les listes de documents
métier.

## Données synchronisées

Le dossier technique contient des fichiers JSON indépendants, pas une base de
données unique :

```
.ms3dprint-manager/
  workspace.json
  clients/<id>.json
  projects/<id>.json
  history/<id>-<date>.json
```

Un enregistrement par client ou projet limite l'impact des conflits de
synchronisation entre ordinateurs. `workspace.json` ne contient que la version
du schéma et l'identifiant stable de l'espace de travail.

Les anciennes versions d'une fiche sont archivées dans `history` avant une
modification explicite. Les fichiers JSON sont écrits dans un fichier temporaire
puis remplacés de manière atomique. En cas de conflit ou d'impossibilité
d'écriture, la fiche précédente est conservée, l'opération échoue clairement et
aucun dossier ni fichier métier n'est modifié.

### Fiche client

Chaque fiche possède un identifiant stable, le code client, le nom normalisé du
dossier et sa localisation relative sous `01_CLIENTS`. Le type est obligatoire
et choisi à la création :

- `Professionnel` : raison sociale, adresse, notes et un contact principal ;
- `Particulier` : nom, prénom, adresse, notes et un contact principal.

Le contact principal est unique en V1 : nom, prénom, fonction facultative,
téléphone et e-mail. Une fiche ne prévoit pas plusieurs contacts à ce stade.
Les champs vides restent autorisés, mais un client doit toujours avoir une
identité affichable et un code client valide.

### Fiche projet

Chaque fiche conserve un identifiant stable, l'identifiant et le code du
client, la référence projet, le nom du dossier, le chemin relatif et le nom
lisible du projet. Elle inclut :

- statut obligatoire : `DEVIS`, `EN_COURS` ou `TERMINE` ;
- date de création ;
- échéance facultative ;
- description ou notes facultatives.

Un nouveau projet démarre avec le statut `DEVIS`. La référence existante reste
générée selon `CODECLIENT-AAAA-XXX_NOM_PROJET`; aucune fiche n'autorise une
référence ou un dossier déjà existant.

## Navigation et interface

La fenêtre principale devient une coque à navigation latérale compacte :

```
Tableau de bord
Clients
Projets
Modèles 3D          (prévu)
Produits            (prévu)
Fournisseurs        (prévu)
Paramètres
```

Le tableau de bord reste l'accueil et donne accès aux actions courantes. Les
modules Clients et Projets deviennent les parcours privilégiés.

### Module Clients

La page Clients fournit :

- recherche par code, entreprise, nom, prénom ou contact ;
- filtre `Tous`, `Professionnels`, `Particuliers` ;
- tableau lisible : identité, code, type, contact principal et nombre de
  projets ;
- bouton de création ;
- clic sur une ligne pour ouvrir une fiche client complète.

La fiche client permet de modifier les informations enregistrées, d'afficher
ses projets, de créer un projet pour ce client et d'accéder à ce projet. Les
dossiers existants sans fiche apparaissent comme « à compléter » plutôt que de
disparaître. Le logiciel ne crée ni ne renomme rien lors de leur découverte.

### Module Projets

La page Projets propose une liste globale avec recherche par référence, nom et
client, ainsi que des filtres par statut, client et année. La fiche projet
présente les informations de suivi, les notes et une vue de ses dossiers et
fichiers.

Cette vue liste les fichiers et dossiers du projet à la demande. Elle permet
d'ouvrir un fichier avec Windows, de naviguer dans les sous-dossiers et
d'utiliser les actions déjà existantes, notamment le classement d'un fichier.
Le bouton « Ouvrir dans l'Explorateur » reste secondaire, jamais obligatoire.

Les dossiers `00_CLIENT` et `99_ARCHIVES` ne sont jamais présentés comme des
projets. Un projet existant reconnu par sa référence mais sans fiche de suivi
reste affiché et peut être complété progressivement.

## Architecture

La logique est découpée en unités sans dépendance WPF :

- `Workspace`: racine active et définition du fournisseur local ;
- `ClientDirectory` et `ProjectDirectory`: lecture ciblée de l'arborescence ;
- `ClientProfileStore` et `ProjectProfileStore`: validation, lecture et écriture
  atomique des fiches synchronisées ;
- services métier : rapprochement dossier/fiche, filtres, recherche et règles
  de création ;
- ViewModels et vues WPF : navigation, liste, fiche et états de chargement.

Les dossiers physiques sont la source de vérité pour les fichiers. Les fiches
ne font qu'enrichir la gestion et ne déclenchent aucun renommage de dossier ou
de fichier métier lors d'une simple édition.

## Sécurité, erreurs et performances

- Aucun fichier métier n'est supprimé, remplacé ou renommé automatiquement.
- Toute opération qui déplace un fichier conserve sa confirmation explicite et
  refuse une destination existante.
- Une racine indisponible, un accès refusé ou un dossier disparu affiche une
  erreur actionnable sans fermer l'application. La dernière liste valide reste
  utilisable quand c'est possible.
- Aucun scan ni polling permanent : chaque module charge son contenu quand il
  est ouvert, les répertoires profonds ne sont lus qu'à la demande et un bouton
  Actualiser invalide le cache.
- Les lectures de fichiers et de dossiers sont asynchrones afin de ne jamais
  bloquer l'interface, en particulier pendant la synchronisation cloud.

## Découpage de livraison

1. Fondation partagée : espace de travail, stockage de fiches, coque de
   navigation, erreurs et cache minimal.
2. Clients : liste, recherche, création, fiche professionnel/particulier,
   contact principal et projets associés.
3. Projets : liste globale, filtres de statut, fiche de suivi et navigation de
   fichiers du projet.
4. Modules ultérieurs : Modèles 3D, Produits et Fournisseurs sur le même
   modèle de collection et de fiche.
5. Évolutions ultérieures : recherche globale, historique fonctionnel,
   devis/factures et automatisation documentaire.

## Vérification

La logique de profils, la compatibilité avec les dossiers existants, les
filtres, la recherche, les écritures atomiques et la protection anti-écrasement
seront couverts par des tests automatisés. Chaque livraison comprend un essai
manuel WPF : redimensionnement, thèmes clair/sombre/automatique, accès refusé,
racine temporairement indisponible et utilisation avec des dossiers déjà
existants.

## Hors périmètre immédiat

- Connexion OAuth ou accès direct aux API Nextcloud, Google Drive ou Dropbox.
- Gestion de plusieurs contacts par client.
- Modification en masse de fichiers, dossiers ou références existantes.
- Remplacement intégral de l'Explorateur pour des opérations système avancées.
