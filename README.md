# MS3DPRINT Manager

MS3DPRINT Manager est une application Windows portable destinée à organiser
les dossiers de MS3DPRINT. Elle est développée avec .NET 10 et Avalonia. La version
publiée est autonome et tient dans un exécutable unique.

## Prérequis

Le SDK .NET 10 est nécessaire pour compiler et exécuter l'application depuis
les sources. L'exécutable publié pour Windows x64 n'a pas besoin d'un runtime
.NET installé.

## Commandes

```powershell
dotnet test MS3DPRINT.Manager.sln
dotnet run --project src/MS3DPRINT.Manager.Windows.App/MS3DPRINT.Manager.Windows.App.csproj
dotnet publish src/MS3DPRINT.Manager.Windows.App/MS3DPRINT.Manager.Windows.App.csproj -p:PublishProfile=PortableWinX64 -o outputs/MS3DPRINT-Manager
```

## Livraison

Après publication, le binaire interne `outputs\MS3DPRINT-Manager\MS3DPRINT.Manager.Windows.App.exe`
est autonome et fonctionne sur Windows x64 sans installation du runtime .NET.
Le nom destiné à l’utilisateur est toujours **MS3DPRINT Manager.exe**.
Copiez le binaire publié sous ce nom dans le dossier de livraison et remplacez
la version précédente. Gardez un seul exécutable dans ce dossier, sans suffixe
Material, numéro de version ou date. Les éventuelles sauvegardes restent dans
`outputs\backups`, séparées de la livraison.

Si l'application a déjà enregistré des codes clients, conservez également
`data\client-codes.json` dans le dossier livré. Ce fichier contient le
registre local nécessaire pour retrouver ces codes.

## Utilisation

**Navigation.** Le menu de gauche donne accès à l’Accueil, aux Projets, Clients, Collections, Filaments,
Estimations, Imprimantes, à la Corbeille et aux Paramètres. `Ctrl+1` à `Ctrl+9` changent de page, `Ctrl+N`
crée un projet et `Ctrl+K` ouvre la recherche. Sous 1100 px de large, le menu se réduit à une barre d’icônes.
Le logiciel retient la taille de sa fenêtre. Un fil d’Ariane cliquable indique où l’on se trouve dans un projet
ou une fiche client.

**Accueil.** Indicateurs (projets, devis, en cours), cinq projets récents, actions rapides et bloc « À surveiller »
(échéances proches ou dépassées, fiches à compléter).

**Projets.** La liste se filtre par texte et par statut, et se trie en cliquant sur un en-tête. Le menu « ⋯ » de
chaque ligne (ou un clic droit) permet d’ouvrir le dossier, modifier la fiche, renommer, déplacer vers un autre
client ou envoyer à la corbeille. Dans un projet : **Nouveau dossier**, **Importer un fichier** (à la racine du
projet, le dossier de destination se choisit ; dans un dossier, le fichier y est importé directement ; un nom
`nom__RÉFÉRENCE` est proposé), puis pour chaque fichier **Ouvrir avec…**, **Renommer**, **Déplacer** et
**Corbeille**. Rien n’est jamais écrasé à destination.

**Clients.** À la création, le **code client est attribué automatiquement** (initiales du nom, puis `AL2`, `AL3`…
en cas de doublon) et s’affiche en direct dans la fenêtre. On peut renseigner le SIRET, l’e-mail, le portable et
l’adresse. Pour un professionnel, la recherche d’entreprise (annuaire public des entreprises) et la recherche
d’adresse proposent des suggestions : la connexion Internet n’est nécessaire que pour elles. La liste affiche le
code, le type de fiche et le nombre de projets ; les clients historiques sans fiche gardent leur code lu dans les
références de leurs projets et se complètent depuis « Modifier la fiche ». Depuis une fiche client, **Nouveau
projet** crée un projet pour ce client.

**Collections, filaments, imprimantes.** Les collections regroupent modèles 3D, produits et fournisseurs (filtre
par type). Un filament a une marque, un nom, une matière, une couleur, un prix au kilo et un indicateur abrasif.
Une imprimante a un prix d’achat, une durée de vie et une puissance : son coût horaire alimente les estimations.

**Estimations.** Choisissez le type de prestation (impression 3D, modélisation seule, ou les deux), le filament
et l’imprimante. L’électricité se calcule en direct à partir de la durée machine, de la puissance de l’imprimante
et du prix du kWh. Calculez, enregistrez, rechargez ou envoyez à la corbeille une estimation.

**Corbeille.** Les suppressions sont réversibles : **Restaurer** remet l’élément à son emplacement d’origine (si
cet emplacement est occupé, libérez-le d’abord). Les références de projet restent réservées.

**Recherche (`Ctrl+K`).** Elle porte sur les noms, codes clients, références de projet, noms de fichiers, e-mails
et téléphones, à la demande et sans indexation. `↑` `↓` puis `Entrée` ouvrent un résultat.

**Aperçus.** Les images s’affichent dans le logiciel. Les PDF s’ouvrent dans une fenêtre intégrée (pages, zoom,
ajustement à la largeur) avec un bouton **Imprimer…** qui ouvre la boîte d’impression de Windows. Les fichiers
STL et OBJ s’ouvrent dans le **visualiseur 3D** (rotation, zoom, déplacement, solide ou filaire, couleur,
dimensions en unités du modèle) ; son test de parois est facultatif et indicatif : il demande l’échelle en
millimètres et un seuil, signale des zones potentiellement fines et n’est pas une garantie d’imprimabilité.
Les autres formats s’ouvrent avec l’application Windows associée (**Ouvrir avec…** pour en choisir une).

**Paramètres.** Emplacement de l’espace de travail, vérification de l’arborescence, état des stockages et
journal des modifications.

**Dépannage.** Une erreur inattendue s’affiche en bas à droite sans fermer le logiciel et est consignée dans
`%LOCALAPPDATA%\MS3DPRINT Manager\erreurs.log`. L’aperçu PDF utilise le moteur intégré à Windows 10 (version
2004 ou plus récente) : aucun navigateur séparé n’est nécessaire. Le journal des modifications est dans
`CHANGELOG.md`.
