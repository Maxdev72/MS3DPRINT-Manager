# MS3DPRINT Manager

MS3DPRINT Manager est une application Windows portable destinée à organiser
les dossiers de MS3DPRINT. Elle est développée avec .NET 8 et WPF. La version
publiée est autonome et tient dans un exécutable unique.

## Prérequis

Le SDK .NET 8 est nécessaire pour compiler et exécuter l'application depuis
les sources. L'exécutable publié pour Windows x64 n'a pas besoin d'un runtime
.NET installé.

## Commandes

```powershell
dotnet test MS3DPRINT.Manager.sln
dotnet run --project src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj
dotnet publish src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj -p:PublishProfile=PortableWinX64 -o outputs/MS3DPRINT-Manager
```

## Livraison

Après publication, distribuez le dossier complet `outputs\MS3DPRINT-Manager`.
L'exécutable `MS3DPRINT.Manager.App.exe` est autonome et fonctionne sur
Windows x64 sans installation du runtime .NET. Copiez ce dossier entier vers
l'emplacement de livraison et lancez l'exécutable depuis ce dossier.

Si l'application a déjà enregistré des codes clients, conservez également
`data\client-codes.json` dans le dossier livré. Ce fichier contient le
registre local nécessaire pour retrouver ces codes.

## Utilisation

- L’interface utilise Material Design avec une palette bleu et gris. Dans
  **Paramètres > Apparence**, choisissez le mode clair, sombre ou automatique
  selon Windows. Le choix reste enregistré pour les prochaines ouvertures.
- **Clients** : ouvrez une fiche pour modifier la raison sociale ou le nom du
  particulier, l’adresse, le contact principal et les notes. Le code client
  et le nom du dossier restent inchangés pour préserver les projets existants.
- Pour un client professionnel, la recherche d'entreprise propose une aide à
  la saisie du nom et du SIRET ; l'adresse peut ensuite être complétée. Un
  particulier n'a pas de champ SIRET. La connexion Internet n'est nécessaire
  que pour ces suggestions.
- Les listes de clients et de projets se rafraîchissent lorsque leurs données
  changent sur le disque, sans recherche ou indexation permanente.
- **Rechercher** : saisissez un nom, un code client, une référence de projet
  ou un nom de fichier, puis appuyez sur Entrée ou sur « Rechercher ». La
  recherche s’exécute à la demande, sans indexation en arrière-plan. Les 200
  premiers résultats sont affichés ; affinez le terme si nécessaire.
- Un résultat client ou projet ouvre sa fiche. Un fichier STL ou OBJ ouvre
  le visualiseur 3D. Les images PNG, JPEG, BMP, GIF et TIFF ainsi que les PDF
  s’affichent en lecture seule dans l’application ; les autres formats
  s’ouvrent via Windows.
- Le visualiseur 3D peut s’ouvrir sans projet : déposez un seul STL ou OBJ
  dans sa fenêtre, ou utilisez « Choisir un fichier… ». Il affiche les
  dimensions X/Y/Z en unités du modèle, sans présumer qu’il s’agit de mm.
- Son test de parois est facultatif : indiquez explicitement l'échelle en
  millimètres et le seuil voulu. Il signale des zones potentiellement fines,
  pas une garantie d'imprimabilité. La mesure est suspendue si le maillage est
  ouvert, non-manifold ou incohérent ; vérifiez toujours le modèle dans le
  trancheur.

L’aperçu PDF utilise le moteur intégré à Windows 10 version 2004 ou plus
récent. Aucun runtime de navigateur séparé n’est nécessaire.
