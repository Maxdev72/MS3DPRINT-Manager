# Livraison de la reprise guidée — intégration serveur

Conception : docs/superpowers/specs/2026-11-04-guided-workspace-reconciliation-design.md.
Guide : docs/reprise-atelier-pc-serveur.md. Branche feature/ms3dforge-remote.

## Points d’entrée

- Core/Migration/WorkspaceReconciliationService : empreintes, comparaison, choix, candidate,
  audit et renouvellement des versions techniques des clients/projets importés.
- PortableWorkspaceMigrationService.Prepare : copie portable et annexe explicite des fiches
  réellement sans dossier. Les octets de la source et les champs inconnus sont conservés.
- Core/Migration/GuidedWorkspaceMigration : sauvegarde privée, journal exclusif sans secret,
  stabilité du PC, transfert et intention durable avant Apply ; reprise par GET uniquement.
- Core/Remote/RemoteWorkspaceClient.Migration : API HTTPS authentifiée dédiée ; rejet explicite
  d’un ancien serveur, contrôle des identités et empreintes des réponses de transfert.
- Desktop/MainWindow.Migration : assistant natif dans les paramètres, confirmation explicite,
  fermeture/annulation et remise de la session au mode serveur après reçu vérifié.
- Server/Migration/WorkspaceMigrationStore : réception privée, sauvegarde cohérente sous le
  verrou métier, candidate sur le volume de l’atelier, bascule récupérable et reçu durable.
- ServerStorage récupère les opérations avant de refuser une racine d’atelier absente.

## Préparer la mise à jour sans toucher la production

Avant la mise à jour du 6 novembre, le serveur utilisait 989d30f3ba11fb7b65787c569d1d894cfda1e8aa.
Le compagnon utilise maintenant 29eb80feaa69d1bd6592c7c54b97c1548e946de9, après sauvegarde
cohérente du volume et déploiement Coolify réussi. Le volume et la racine de l’atelier existant
sont conservés. Aucun atelier PC réel n’a encore été transféré par ce nouvel assistant.
Les projets MS3DPRINT-FR et MS3DPRINT-SHOP restent hors périmètre.

L’image candidate utilise le Dockerfile existant src/MS3DPRINT.Manager.Server/Dockerfile,
contexte racine du dépôt. Le stage verify exécute la suite serveur. Préparer et valider cette
candidate avant de modifier le service du compagnon ; conserver une sauvegarde complète
privée de son volume et l’image précédente pour retour arrière.

Le volume persistant doit contenir le parent commun des dossiers d’atelier, des candidates
et des anciens ateliers. Le montage documenté sur /data satisfait cette condition. Ne pas
monter directement la racine d’atelier comme point de montage : sa bascule exige de pouvoir
renommer ce dossier. L’utilisateur app doit pouvoir écrire dans son parent.

Conserver les valeurs existantes Server__StateRoot et Server__WorkspaceRoot, les clés,
identity.sqlite et commands.sqlite. Ne pas réinitialiser le propriétaire ni remplacer son
état privé. La configuration de confiance du proxy reste inchangée. Les routes de reprise
exigent la même authentification et la même protection CSRF que les routes métier.

La limite générale des requêtes reste 8192 octets. Seuls les morceaux (512 Kio) et les choix
(4 Mio, 10 000 conflits maximum) ont une limite dédiée. Les sauvegardes et journaux privés
ne sont pas servis par le site. Les réponses ne contiennent pas de chemins absolus du serveur.

Après la future mise à jour, vérifier santé, accès anonyme refusé et capabilities avec une
session native. Faire d’abord une reprise avec atelier fictif. La reprise métier réelle devra
montrer son bilan et recevoir sa confirmation finale depuis Manager.

## Vérifications

Les preuves de cette session sont dans outputs/guided-workspace-reconciliation/ (hors Git).
Les tests utilisent uniquement des ateliers fictifs : comparaison, annexes, chemins, versions,
CSRF/HTTPS/limites, coupures, dérive serveur, commandes Pending et récupération de bascule.
Aucune donnée d’atelier réelle n’est incluse dans les images ou le dépôt.

Le moteur Docker local est indisponible au début de cette session. La compilation et les tests
Linux passent par Ubuntu sous WSL ; une image Docker n’est pas réputée construite sans
preuve de build distincte. Les tests du serveur Linux ne remplacent pas le test de son proxy
et de son volume réel lors de la future mise à jour.

Validation finale : image locale ms3dforge-compagnon:29eb80f construite, démarrage sous app vérifié, stage de vérification Docker 124 tests réussis via cache hors ligne. Voir docs/reprise-guidee-validation.md. Déploiement du compagnon réalisé ensuite le 6 novembre ; voir la validation actualisée.


## Simplification du premier envoi — 6 novembre 2026

Le panneau natif enchaîne connexion, sauvegarde, préparation portable et comparaison.
Une préparation bloquée uniquement par MissingCollectionFolder est reprise avec une
annexe privée des identifiants signalés ; le service portable revalide chaque sélection.
Le bilan final indique leur conservation inactive et les entrées hors périmètre, sans
modifier la source. Les autres anomalies restent bloquantes.

Les conflits gardent un choix explicite, individuel ou groupé pour le PC. Sans conflit,
la candidate est auditée directement. La confirmation finale reste obligatoire et
Annuler/Fermer conserve le focus. Après reçu vérifié et source stable, le panneau remet
la session au mode serveur existant sans nouvelle connexion. Un reçu incertain reste
consultable par GET uniquement ; une source modifiée empêche la bascule automatique.
L’API de reprise et le serveur déployé ne changent pas. Le premier envoi simplifié ne
constitue pas une synchronisation bidirectionnelle continue des fichiers locaux.

## Dossiers historiques serveur sans fiche

Compare transforme uniquement les anomalies ProjectProfileMissing de l’instantané serveur,
pour un dossier effectivement inventorié, en informations PreservedProjectFolder liées
au plan. Capture et les audits d’export/préparation PC restent stricts. CaptureForReconciliation
n’accepte que les dossiers explicitement signalés par ce plan validé ; toutes les autres
anomalies, les empreintes et les contrôles de chemins restent vérifiés. Cette politique est
appliquée à la candidate, aux ancres avant activation et à la récupération de bascule.
Les fichiers sont conservés octet pour octet ; aucune métadonnée projet n’est fabriquée.

Prepare peut réexaminer une ancienne opération bloquée uniquement par ProjectProfileMissing,
sans reçu et sans journal de bascule. Les empreintes du PC préparé, du serveur actif et de
sa sauvegarde doivent encore correspondre. Le verrou métier, les commandes Pending et les
limites de choix sont vérifiés comme pour une première préparation. Le client redemande
uniquement cette préparation ; il ne renvoie ni les morceaux ni une activation incertaine.
Le panneau n’affiche plus les choix de versions pour une préparation bloquée et montre
les noms des éléments concernés. Les dossiers historiques apparaissent aussi au bilan final.
