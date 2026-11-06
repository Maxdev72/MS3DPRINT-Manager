# MS3DPRINT Manager sous Ubuntu et Linux Mint

Version native x86-64. .NET est inclus : aucune installation de .NET nécessaire.

## Installation

Installer les dépendances du bureau si elles manquent :

```sh
sudo apt install libicu-dev libfontconfig1 libice6 libsm6 libx11-6 libxrandr2 libxi6 libxcursor1 libgl1 xdg-utils libsecret-tools libgtk-3-0 libwebkit2gtk-4.1-0 libsoup-3.0-0
```

Extraire `MS3DPRINT-Manager-linux-x64.tar.gz` dans un dossier personnel, puis lancer
`MS3DPRINT Manager`. Depuis un terminal dans ce dossier :

```sh
chmod +x './MS3DPRINT Manager'
'./MS3DPRINT Manager'
```

La première extraction des bibliothèques natives utilise le dossier .NET habituel
`~/.net`. Les réglages sont dans `~/.local/share/MS3DPRINT Manager/data`
(ou le dossier indiqué par `XDG_DATA_HOME`). L’atelier local se choisit dans
les réglages ; le chemin proposé est `~/MS3DPRINT`.

## Données et connexion

Ne pas recopier les chemins `C:\...` dans Linux. Pour un atelier Windows existant,
utiliser une sauvegarde portable et une copie séparée. Le lancement Linux ne migre
pas les données du PC et ne met pas automatiquement le serveur à jour.

La connexion mémorisée utilise le trousseau du bureau, via Secret Service
(`libsecret-tools`). Le trousseau doit être déverrouillé. Aucun mot de passe ni
jeton n’est sauvegardé en clair ; si libsecret-tools n’est pas installé, décocher « Mémoriser ». Un trousseau verrouillé doit être déverrouillé, y compris pour effacer une connexion mémorisée.
L’ouverture des documents utilise les applications associées du bureau.

## Limites de cette première livraison Linux

- Les PDF contenant du texte sont analysables. L’OCR des photos et PDF numérisés
  repose encore sur Windows et n’est pas disponible dans cette livraison Linux.
- La détection automatique de Bambu Studio/OrcaSlicer est encore Windows ;
  utiliser les associations de fichiers Linux pour ouvrir les modèles dans son slicer.
- Après import, la suppression sécurisée du fichier source externe reste Windows.
  Sous Linux la copie importée est conservée et la source reste en place.
- Le navigateur de modèles intégré utilise WebKitGTK sous Ubuntu (bibliothèques incluses dans la commande ci-dessus). Son fonctionnement reste à tester sur un bureau réel ; le navigateur externe reste utilisable.
- Les fonctions disponibles en mode serveur restent celles de la version Windows
  actuelle : la compatibilité Linux ne termine pas la synchronisation en cours.

Validation : compilation et tests sous Ubuntu 26.04 via WSL. Linux Mint et un bureau
Linux réel restent à valider ; cette archive est une première version à tester.

## Compilation

Avec le SDK .NET 10 et les dépendances ci-dessus, à la racine du dépôt :

```sh
bash scripts/publish-linux.sh
```

L’archive se trouve dans `outputs/`. La livraison Windows reste inchangée.

Dépendances du navigateur : [documentation Avalonia](https://docs.avaloniaui.net/docs/app-development/embedding-web-content#linux).
