# Sauvegarde et restauration — plan

**Objectif :** sauvegarder les données métier et les documents optionnels depuis les paramètres, puis restaurer une copie vérifiée dans un nouveau dossier.

**Architecture :** service ZIP versionné dans Core/Storage, manifeste avec SHA256 et aperçu vérifié, chemins relatifs et liste fermée des dossiers métier. Restauration dans un dossier temporaire voisin, publication par renommage uniquement vers un emplacement inexistant. Le poste conserve ses préférences locales (performances, emplacement actif, géométrie de fenêtre).

**Périmètre :** `.ms3dprint-manager`, le registre métier `data/client-codes*.json` (actif et previous) et les dossiers de `FolderTemplates.Main`. Aucun autre dossier à la racine, notamment le dépôt de développement, n'est inclus. Sans documents : conserver les métadonnées et l'arborescence métier, exclure fichiers métier et corbeille. Les réglages atelier sont inclus. Aucun basculement automatique d'espace après restauration.

**Contraintes :** conserver le travail non committé, ne pas lire ni sauvegarder les données réelles pour les essais, confirmer la restauration, ne jamais écraser des fichiers existants. Une seule livraison `MS3DPRINT Manager.exe`, binlogs uniques à chaque commande .NET.

## Étapes
- [x] Tests Core : aller-retour, mode sans documents, exclusions, archive altérée, chemins dangereux, destination existante, erreur sans archive partielle.
- [x] Service `WorkspaceBackupService.cs` : création, inspection, restauration ; historique du dernier succès géré par le parcours des paramètres. Tests rouges avant implémentation.
- [x] Carte `MainWindow.Settings.Backups.cs` dans Espace et stockage ; choix documents, sauvegarder, restaurer, statut. Aperçu/confirmation `RestoreBackupWindow.cs`, opérations en arrière-plan et garde contre double lancement.
- [x] Tests UI réels en environnement headless, captures à 960/1600 px et contrôle de la confirmation/annulation.
- [x] Revue ciblée, suite complète, publication, remplacement et empreinte de livraison.
- [x] Documentation utilisateur et rapport de livraison avec limites explicites.

## Vérification
`dotnet test MS3DPRINT.Manager.sln '-bl:outputs/binlogs/backup-full-{}.binlog'`

Publication PortableWinX64 selon AGENTS.md. Les archives ne sont pas chiffrées ; elles doivent être conservées dans un emplacement approprié. Les fichiers modifiés pendant l'opération provoquent son échec lorsque la modification est détectée ; il ne s'agit pas d'un instantané système transactionnel.
