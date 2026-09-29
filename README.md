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

Le fichier `data\client-codes.json` doit voyager avec l'exécutable afin de
conserver les codes clients enregistrés. Lors de la livraison, copiez le
dossier publié complet, y compris ce fichier s'il a été créé.
