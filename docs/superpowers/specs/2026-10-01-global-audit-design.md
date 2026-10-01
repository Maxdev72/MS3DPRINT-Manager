# Audit global et fiabilisation — conception

## Objectif

Fiabiliser MS3DPRINT Manager avant d'ajouter de nouveaux modules, sans
modifier les dossiers métier, les noms existants ni les fichiers utilisateur.

## Constats vérifiés

- Les écritures de profils client/projet sont atomiques et historisées, mais
  une fiche projet dupliquée pour le même dossier reste techniquement
  possible entre deux instances de l'application.
- Les catalogues Clients et Projets lisent directement Nextcloud sur le fil
  de l'interface. Une erreur de synchronisation, de droit ou de JSON peut
  remonter depuis l'événement WPF de chargement et fermer l'application.
- Les catalogues et le navigateur de fichiers sont synchrones : cela reste
  acceptable pour une petite arborescence, mais sera à rendre asynchrone et
  annulable avant un catalogue volumineux ou un stockage distant.
- Les contrôles de thème, calendrier et défilement sont maintenant centralisés
  dans `App.xaml`. Les tests XAML vérifient leur présence mais pas un rendu
  pixel-à-pixel, qui reste à valider lors des recettes Windows.
- La publication autonome ne comporte aucune vulnérabilité NuGet déclarée.
  Deux dépendances transitives vulnérables ne concernent que le projet de
  tests, via ses outils de test anciens.

## Découpage retenu

1. **Résilience des données et de la navigation** — empêcher les doublons de
   fiches projet et contenir toutes les erreurs de catalogue dans l'interface.
2. **Réactivité Nextcloud** — charger les catalogues et les listes de fichiers
   hors du fil UI, avec indicateur de chargement et annulation des résultats
   obsolètes.
3. **Qualité de livraison** — moderniser les dépendances de test, uniformiser
   le formatage, compléter la recette Windows et documenter les limites des
   futurs connecteurs de stockage.

## Contraintes

- Windows local, .NET 8/WPF, exécutable autonome sans dépendance payante.
- Aucun fichier existant ne doit être écrasé ou supprimé par ces améliorations.
- Les données restent dans l'espace de travail MS3DPRINT et les sauvegardes
  de profils restent consultables.
- Toute écriture doit conserver les protections actuelles contre les conflits.

## Résultat du lot 1 — 1 octobre 2026

- Une seconde fiche pour un même dossier ou une même référence projet est
  désormais refusée par le stockage des profils.
- Les lectures Clients et Projets qui échouent (accès, synchronisation ou JSON)
  restent contenues dans l'écran concerné avec un message exploitable.
- La suite complète contient 106 tests réussis et la solution compile sans
  avertissement ni erreur.
- La publication Windows x64 autonome a été générée puis installée après
  vérification de son empreinte SHA-256.
