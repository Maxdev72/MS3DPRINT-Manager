# Gestion complète et bibliothèque de filaments

Architecture validée par l’utilisateur dans ce chat le 7 octobre 2026.

- Application .NET 8 / WPF, stockage de documents existant conservé.
- Les métadonnées restent dans `.ms3dprint-manager` de l’espace de stockage,
  pour suivre la synchronisation Nextcloud. Pas de serveur ni de nouvelle base.
- Clients, projets, modèles, produits et fournisseurs : créer, lire, modifier,
  renommer, déplacer dans l’espace géré, supprimer vers la corbeille et restaurer.
- Codes clients, identifiants et références projets restent stables. Un changement
  de client d’un projet change son rattachement sans régénérer sa référence.
- Déplacements : client dans 01_CLIENTS, projet dans un client existant,
  collection dans sa catégorie ; fichiers dans l’espace géré. Les chemins
  enregistrés permettent de retrouver les éléments déplacés.
- Fournisseurs : nom, description, contact, adresse, téléphone, e-mail, site,
  notes et documents. Modèles et produits : nom, description, notes et documents.
- Fichiers et sous-dossiers : créer un dossier, importer, ouvrir, renommer,
  déplacer, envoyer à la corbeille. Même comportement dans les fiches.
- Corbeille interne : conserver ensemble documents et métadonnées liées.
  Restauration à l’emplacement d’origine, sans écrasement d’un chemin existant.
  Suppression d’un client inclut ses projets ; confirmation explicite dans l’UI.
- Filaments : marque, nom, matière/type, prix en euros par kg (decimal >= 0),
  abrasif oui/non. Ajouter, consulter, modifier, dupliquer, filtrer et restaurer.
  Pas de gestion de stock inventée dans cette version.
- Services communs : refuser les chemins hors espace, collisions et déplacements
  dans soi-même ; refuser les liens et jonctions (les placeholders Windows Cloud Files sont autorisés) ; préserver les données
  lorsqu’une opération échoue. Tester uniquement dans des dossiers temporaires.
- Interface Material Design et tous les thèmes conservés. Pas de perte d’une
  fiche non enregistrée pendant une navigation ou une opération de gestion.
- Livraison : un seul `MS3DPRINT Manager.exe`, remplacer la version précédente,
  préserver `data`, `.ms3dprint-manager` et tous les documents de l’utilisateur.

## Décisions d’exécution

Les nouveaux champs de chemin sont optionnels pour lire les anciens profils.
Les dossiers sans fiche restent visibles ; l’édition adopte le dossier existant.
La corbeille n’offre pas de purge définitive dans cette livraison.
Les tâches indépendantes sont confiées à des agents ; l’intégration, les builds
et la livraison sont orchestrés dans le dépôt `sources/` de ce chat.
