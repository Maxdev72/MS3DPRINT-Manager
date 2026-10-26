# Application compagnon Android

Mise à jour : 25 octobre 2026 (version 1.1).

## Rôle
Le téléphone sert à **noter sur le terrain** (achats, recettes, photos de tickets, factures PDF) et à **consulter** l’atelier (projets, activité, seuils). Il n’écrit jamais dans les registres du PC :

- il dépose de petits fichiers (`achat-….json`, `recette-….json` et leur photo ou PDF) dans le dossier « Echanges mobile » de l’espace de travail, synchronisé par Nextcloud ;
- le PC lit ces fichiers et propose de les importer (boutons « Téléphone » des pages Achats et Recettes) : rien n’entre dans le livre ou le registre sans confirmation ;
- en sens inverse, le PC publie `etat-pc.json` dans le même dossier (projets, clients, activité de l’année, seuils, fournisseurs récents). Le téléphone le lit, sans jamais le modifier.

## Écrans
Les composants visuels communs (cartes à section, puces, indicateurs, logo, carte de synthèse) sont dans `Ui/Look.cs`.

- **Saisir** : Achat ou Recette. Date, fournisseur ou client (suggestions), libellé, montant (clavier numérique), règlement, pièce, projet, pièce jointe (appareil photo, galerie ou PDF). Les erreurs s’affichent sous le champ concerné. Une recette peut être mixte (part marchandises).
- **Projets** : recherche, filtres (Actifs, Devis, En cours, Terminés, Archivés), fiche avec échéance, priorité et bilan (estimé, encaissé, reste, achats). Depuis la fiche, « Noter un encaissement » ou « Noter un achat » préremplit la saisie (le reste à encaisser est proposé comme montant).
- **Activité** : chiffre d’affaires encaissé de l’année, marchandises et prestations, achats payés, graphique mois par mois, seuils d’activité (barres de progression), nombre de saisies à importer.
- **Envois** : historique avec l’état de chaque saisie (Envoyé, En attente, À renvoyer), bouton « Réessayer l’envoi », suppression d’une saisie non envoyée (avec confirmation).
- **Réglages** : choix du dossier d’échange, test d’accès, actualisation de l’état du PC, explications.

## Lecture des documents
Une photo, une capture d’écran, une image de la galerie ou un PDF (par la galerie ou « Partager ») est lu sur le téléphone : ML Kit (modèle intégré à l’application, pas de réseau) reconnaît le texte, puis le même moteur que sur le PC (`Core/Recognition`) trouve montant, date, numéro, fournisseur ou client, règlement. Achat ou recette est choisi selon le document quand il est identifiable. Seuls les champs vides, ou remplis par une lecture précédente, sont renseignés : ce que vous avez tapé n’est jamais écrasé. Un bandeau indique ce qu’il faut vérifier en priorité.

## Fonctions utiles
- **Hors ligne** : une saisie est d’abord enregistrée sur le téléphone puis envoyée. Si le dossier est injoignable, elle attend et repart toute seule (au démarrage, au retour dans l’application ou avec « Réessayer »).
- **Facture reçue par e-mail** : depuis une autre application, « Partager » puis « MS3DPRINT Compagnon ». Le PDF est lu sur le téléphone (même lecteur que sur le PC) : numéro, montant, client et nature sont préremplis, il reste à indiquer la date de paiement.
- **Robustesse** : la saisie en cours est conservée si Android ferme l’application (par exemple pendant que l’appareil photo est ouvert), et la photo prise est retrouvée au retour.
- **Bouton retour** : ferme une feuille, puis une fiche projet, puis revient à « Saisir », puis met l’application en arrière-plan.

## Première utilisation
1. Installer `MS3DPRINT Compagnon.apk` (autoriser l’installation depuis la source utilisée ; sur Samsung, désactiver temporairement Auto Blocker).
2. Vérifier dans Nextcloud que le dossier « Echanges mobile » de l’espace de travail est synchronisé sur le téléphone.
3. Dans l’application : Réglages > « Choisir le dossier d’échange », sélectionner ce dossier. Le choix est mémorisé.
4. Sur le PC : laisser MS3DPRINT Manager ouvert quelques minutes pour qu’il publie l’état de l’atelier (Paramètres > Atelier > Application mobile permet de le publier tout de suite ou de désactiver la publication).

## Fabrication
Le projet `src/MS3DPRINT.Manager.Android` n’est volontairement pas dans `MS3DPRINT.Manager.sln` : il demande la charge de travail .NET Android et un JDK. Installés le 25 octobre 2026 : `dotnet workload install android`, JDK 21 (Microsoft OpenJDK), plateforme Android SDK 36.

```bash
dotnet build src/MS3DPRINT.Manager.Android -c Release -p:JavaSdkDirectory="C:/Program Files/Microsoft/jdk-21.0.12.101-hotspot" -p:AndroidSdkDirectory="C:/Users/<utilisateur>/AppData/Local/Android/Sdk" -p:AcceptAndroidSDKLicenses=True
```

Fichier produit : `src/MS3DPRINT.Manager.Android/bin/Release/net10.0-android/fr.ms3dprint.compagnon-Signed.apk` (arm64 et x86_64, signé avec la clé de débogage : suffisant pour une installation directe, pas pour une publication).

## Test sur émulateur
Image Android 16 (x86_64, Google APIs) créée avec `avdmanager` sous le nom `compagnon_test`.

```bash
python tools/android/smoke.py
```

Le script installe l’application à neuf, choisit le dossier d’échange avec le sélecteur Android, vérifie la lecture de l’état du PC (`tools/android/etat-pc-exemple.json`, données fictives), note un achat et contrôle le fichier déposé : 12 vérifications. Il vérifie aussi le bouton retour. Limites de `adb` : le texte saisi doit être sans accent.

Autres parcours vérifiés à la main sur l’émulateur : appareil photo, envoi avec photo, facture PDF réelle lue et préremplie (par la galerie et par « Partager », y compris application fermée), dossier devenu inaccessible puis retrouvé, saisie et photo récupérées après un arrêt forcé, validations, suppression avec confirmation.

## Pièges rencontrés
- `AndroidLinkMode=None` fait avorter le moteur Mono au démarrage (« class-init … instance_size ») : ne pas le remettre.
- L’activité d’Avalonia exige un thème AppCompat (`Theme.AppCompat.NoActionBar`).
- Le projet ne peut pas avoir un espace de noms `MS3DPRINT.Manager.Android` : il masque les types `Android.*` ; l’espace de noms est `MS3DPRINT.Manager.Companion`.
- Les icônes sont tracées en contour (un remplissage cache les lignes ouvertes).
- Android 13+ n’appelle plus `OnBackPressed` : le bouton retour passe par `OnBackPressedDispatcher`.
- Les applications Java de l’émulateur ne voient pas les certificats que Windows accepte (antivirus qui inspecte le trafic) : lancer `sdkmanager` avec `JAVA_TOOL_OPTIONS=-Djavax.net.ssl.trustStoreType=Windows-ROOT`.

## Limites connues
- Non essayée sur un téléphone réel : la partie arm64 n’a pas pu être exécutée sur l’émulateur Intel.
- Les saisies de type « devis » et la modification d’une ligne déjà envoyée se font sur le PC.
- Les projets et clients sont une copie : l’état n’est rafraîchi que lorsque le PC est ouvert.
