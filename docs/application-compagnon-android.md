# Application compagnon Android

Date : 25 octobre 2026.

## Rôle
Noter un achat depuis le téléphone, photographier le ticket, et l’envoyer au PC. L’application n’écrit jamais dans les registres : elle dépose deux fichiers (`achat-AAAA-MM-JJ-xxxxxxxx.json` et `.jpg`) dans le dossier d’échange « Echanges mobile » de l’espace de travail, synchronisé par Nextcloud. Le PC les importe après confirmation (page Achats > « Téléphone »).

## Première utilisation
1. Installer le fichier `MS3DPRINT Compagnon.apk` (autoriser l’installation depuis cette source si Android le demande).
2. Dans Nextcloud, vérifier que le dossier « Echanges mobile » de l’espace de travail est synchronisé sur le téléphone.
3. Dans l’application, en bas de l’écran : « Choisir le dossier d’échange », puis sélectionner ce dossier. Le choix est mémorisé.
4. Saisir l’achat, « Photographier le ticket », « Envoyer au PC ».

## Fabrication
Le projet `src/MS3DPRINT.Manager.Android` n’est volontairement pas dans `MS3DPRINT.Manager.sln` : il demande la charge de travail .NET Android et un JDK. Prérequis installés le 25 octobre 2026 : `dotnet workload install android`, JDK 21 (Microsoft OpenJDK), plateforme Android SDK 36.

```bash
dotnet build src/MS3DPRINT.Manager.Android -c Release -p:JavaSdkDirectory="C:/Program Files/Microsoft/jdk-21.0.12.101-hotspot" -p:AndroidSdkDirectory="C:/Users/<utilisateur>/AppData/Local/Android/Sdk" -p:AcceptAndroidSDKLicenses=True
```

Le fichier à installer est `src/MS3DPRINT.Manager.Android/bin/Release/net10.0-android/fr.ms3dprint.compagnon-Signed.apk` (signé avec la clé de débogage : suffisant pour une installation directe, pas pour une publication).

## Vérifications faites
Essayée dans un émulateur Android 16 (x86_64) : démarrage, choix du dossier d’échange (mémorisé après redémarrage), saisie, appareil photo, validation des champs, envoi d’un achat avec photo ; les deux fichiers produits sont lus correctement par le PC. La partie arm64 (téléphones réels) utilise la même configuration mais n’a pas pu être exécutée sur l’émulateur.

## Pièges rencontrés
- `AndroidLinkMode=None` fait avorter le moteur Mono au démarrage (« class-init … instance_size ») : ne pas le remettre.
- L’activité d’Avalonia exige un thème AppCompat (`Theme.AppCompat.NoActionBar`).
- Le dossier ne peut pas s’appeler `MS3DPRINT.Manager.Android` côté espace de noms : il masque les types `Android.*` ; l’espace de noms est `MS3DPRINT.Manager.Companion`.

## Limites connues
- Seuls les achats sont gérés ; les recettes et la consultation des projets sont prévues ensuite.
- Le clavier numérique n’est pas forcé pour le montant.
- Poids : environ 34 Mo.
