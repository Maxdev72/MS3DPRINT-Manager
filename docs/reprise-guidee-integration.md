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

Le serveur documenté en production reste sur 989d30f3ba11fb7b65787c569d1d894cfda1e8aa.
Cette livraison de code n’est ni un déploiement, ni une nouvelle migration réelle.
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
