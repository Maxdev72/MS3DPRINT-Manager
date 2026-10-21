# Audit de la logique métier entre pages et actions — 19 octobre 2026

Audit en lecture seule : aucun fichier de code, de données ou de livraison n'a été modifié. Seul ce rapport a été ajouté.

## Périmètre et méthode

Relu en entier : `MainWindow.axaml.cs`, `.Navigation`, `.Dashboard`, `.Projects`, `.ProjectFiles`, `.Clients`, `.Estimates`, `.Viewer`, `.Trash`, `.Search`, `.Printers`, `.Filaments`, `.Collections`, `.Settings` ; côté Core : `EntityManagementService`, `ClientCodeChanger`, `ManagedFileService`, `ProjectCatalog`, `ProjectCreationService`, `ProjectProfileStore`, `ProjectReferenceReservations`, `ClientCatalog`, `ClientCreationService`, `ClientCodeRegistry`, `EstimateStore`, `EstimateCalculator`, `FilamentStore`, `PrinterStore`, `GlobalSearchService`, `WorkspaceLocation`, et les `Workspace*` du Desktop.

Un constat (B1) a été **reproduit par exécution** sur un espace temporaire. Les autres viennent de la lecture du code et sont marqués « lecture ».

Non fait : le logiciel n'a pas été lancé, la suite de tests n'a pas été rejouée, `ModelViewerView` et `ModelBrowserView` n'ont pas été relus. Les audits précédents (7, 8 et 13 octobre) ne sont pas répétés ; les points déjà corrigés (M01 à M11) n'ont pas été retrouvés comme régressions.

## Carte des liens entre pages

| Donnée | Créée dans | Utilisée par | Nature du lien |
|---|---|---|---|
| Client | Clients | Projets, Recherche, Estimations (libellé) | identifiant dans la fiche projet ; **nom de dossier** dans l'estimation |
| Projet | Projets, Fiche client | Accueil, Recherche, Estimations, Visualiseur (rangement des captures) | référence texte `XX-AAAA-NNN` |
| Estimation | Estimations, Visualiseur | Estimations seule | **références texte** : `ProjectReference`, `ClientName`, nom de filament, nom d'imprimante |
| Filament / Imprimante | Filaments, Imprimantes | Estimations, Visualiseur | nom affiché, pas d'identifiant |
| Corbeille | toutes les pages | Corbeille | manifeste + fiches « compagnes » |
| Registre des codes | création de client | **personne** (voir B6) | clé = nom de dossier |

Il n'existe aucun lien depuis le projet vers ses estimations, ni de passage devis → projet : la page Estimations est une île.

## Constats

Gravité : **É** élevée (perte ou blocage), **M** moyenne, **F** faible, **i** information.

### Intégrité des données et actions bloquées

| # | Gravité | Constat | Où | Recommandation |
|---|---|---|---|---|
| B1 | **É** | **Impossible d'envoyer une estimation ou une imprimante à la corbeille.** `ManagedFileService.IsProfile` n'accepte que `clients`, `projects`, `collections`, `filaments` ; `estimates` et `printers` lèvent « Les métadonnées sont protégées. ». Reproduit : `EstimateStore.Trash` et `PrinterStore.Trash` échouent tous deux. Les boutons de la page Estimations et de la page Imprimantes affichent donc une erreur à chaque fois. Aucun test ne couvre ces deux cas. | `ManagedFileService.cs:338`, `EstimateCalculator.cs:57`, `PrinterStore.cs:85` | Ajouter `estimates` et `printers` à la liste, puis tests de corbeille + restauration pour chaque type. |
| B2 | **É** | **Une estimation chargée perd son lien projet / filament / imprimante si l'on change de page puis revient.** Chaque entrée dans la page rappelle `LoadEstimateFilamentsAsync`, qui remet projet à vide, filament et imprimante au premier élément, mais conserve `_loadedEstimate` et les montants. Un « Enregistrer » ensuite écrase la fiche existante avec un autre filament, une autre imprimante et **sans projet**. | `MainWindow.Estimates.cs:22-49`, `:222-245`, `MainWindow.Navigation.cs:142` | Ne recharger que les listes en conservant la sélection (par identifiant), ou réinitialiser aussi `_loadedEstimate` ; avertir avant d'abandonner une estimation modifiée. |
| B3 | **É** | **Le périmètre « Modélisation seule » n'est pas enregistré.** `SaveEstimate_Click` décide du libellé par `filament is null`, pas par `EstimateScopeIndex`. Or la liste des filaments garde sa sélection par défaut même quand les champs d'impression sont masqués : une estimation de modélisation est donc enregistrée avec un filament, une imprimante, 100 g et 2 h inventés. Relue, elle revient en mode « Impression ». Dans l'autre sens, une estimation d'impression dont le filament a été renommé ou supprimé s'enregistre « Modélisation seule ». | `MainWindow.Estimates.cs:174`, `:235` | Stocker un champ explicite `Scope`, vider filament, imprimante, poids et heures en modélisation seule, ne jamais déduire le périmètre d'un libellé. |
| B4 | M | **Lien estimation ↔ projet fragile.** L'estimation garde `Reference` + nom de dossier client. Renommer le client ou déplacer le projet vers un autre client rompt la correspondance (`project.ClientFolderName == estimate.ClientName`) : le projet n'est plus retrouvé à la relecture, puis B2 efface le lien à l'enregistrement. `ClientCodeChanger` met à jour la référence mais pas `ClientName`, et ignore sans le dire les estimations illisibles (`LoadReadable`). Les estimations restent aussi après mise à la corbeille du projet ou du client. | `EstimateCalculator.cs:30`, `MainWindow.Estimates.cs:175`, `ClientCodeChanger.cs:473,505` | Lier par `ProjectId` (garder la référence pour l'affichage) ; propager renommage, déplacement et corbeille ; signaler les estimations non mises à jour. |
| B5 | M | **Client historique sans code : fiche impossible à compléter.** `WorkspaceClientReader` construit le catalogue **sans** registre des codes ; un client sans projet référencé a donc un code vide. « Compléter la fiche » reprend ce code vide et `CompleteExisting` refuse (« Le code client est obligatoire »). « Changer le code » est refusé tant que la fiche n'est pas complétée. Impasse. | `WorkspaceClientReader.cs`, `CreateClientWindow.cs:52`, `WorkspaceClientCreator.cs:157` | Proposer un code (`ClientCodeSuggester.SuggestUnique`) quand il est vide, et le contrôler contre les codes existants. |
| B6 | M | **Le registre des codes (`data/client-codes.json`) n'est jamais relu et n'est jamais tenu à jour.** `ClientCatalog` reçoit `legacyCodes = null` partout ; `GetCode` n'est appelé nulle part. Le registre n'est pas modifié par renommage de client, changement de code ni corbeille. Il sert donc seulement de réservation de nom : après renommage ou mise à la corbeille, **recréer un client du même nom est refusé** (« Un code est déjà enregistré… ») alors qu'il n'existe plus. Il ne garantit pas non plus l'unicité des **codes** (clé = nom). | `ClientCatalog.cs:12,69`, `ClientCodeRegistry.cs:62-90` | Décider : supprimer le registre, ou le faire vivre (lecture, mise à jour sur renommage / changement / corbeille / restauration). Appliquer l'unicité du code dans le Core, pas seulement dans la fenêtre. |
| B7 | M | **Doublon de code possible par la corbeille.** `UsedCodes` ignore les clients en corbeille ; créer un client avec le code libéré puis restaurer l'ancien donne deux clients de même code. Les références restent protégées (réservations), mais le code n'identifie plus un client. | `WorkspaceClientCreator.cs:178`, `ManagedFileService.Restore` | Refuser la restauration d'un client si son code est pris, ou lui proposer un nouveau code. |

### Cohérence entre pages

| # | Gravité | Constat | Où | Recommandation |
|---|---|---|---|---|
| B8 | M | **Compteurs et pastilles non rafraîchis.** `PreloadAsync(force)` n'est appelé qu'à l'arrivée sur Accueil, au changement de statut et aux réglages. Créer, renommer, déplacer, supprimer ou **restaurer** un projet ou un client ne met pas à jour les pastilles du menu (projets, clients) avant le prochain passage par Accueil. Restaurer depuis la corbeille ne recharge que la corbeille. | `MainWindow.Projects.cs`, `.Clients.cs`, `.Trash.cs:50` | Un point unique « données modifiées » qui rafraîchit pastilles et Accueil. |
| B9 | F | **« À faire » n'a pas le même sens partout.** Le compteur d'Accueil ne compte que les projets avec fiche au statut `Quote` ; la liste Projets, pour le même libellé, ajoute les projets sans fiche. La somme À faire + En cours + Terminés n'égale pas le total. Le statut interne s'appelle encore `Quote` (devis) alors que l'interface dit « À faire ». | `WorkspaceDashboardReader.cs:96`, `MainWindow.Projects.cs:121-126`, `ProjectStatus.cs` | Compter les projets sans fiche comme « À faire » partout, ou afficher un quatrième compteur « À compléter ». Renommer `Quote` en profondeur seulement si la migration des JSON est prévue. |
| B10 | F | **Recherche globale : un clic sur un client n'ouvre pas sa fiche** (il charge la liste et change seulement le sous-titre). La recherche ne couvre ni collections, ni filaments, ni imprimantes, ni estimations, alors que la barre est présentée comme globale. | `MainWindow.Search.cs:109-115` | Ouvrir `LoadClientDetailAsync` ; élargir ou nommer le périmètre. |
| B11 | F | **Changer d'espace de travail ne réinitialise pas l'état en mémoire** : estimation chargée, liste de projets, visualiseur, client ouvert. Une estimation de l'ancien espace peut être enregistrée dans le nouveau. | `MainWindow.Settings.cs:49-56,100-105` | Réinitialiser les pages à l'activation d'un espace. |
| B12 | F | **Aucune alerte de dépendance** : mettre un filament ou une imprimante à la corbeille ne dit pas combien d'estimations les citent ; l'estimation garde des noms (le prix est bien figé dans les montants, ce qui est sain) mais ne retrouve plus la sélection à la relecture. | `MainWindow.Filaments.cs`, `MainWindow.Printers.cs` | Lier par identifiant, afficher l'usage avant suppression. |

### Calculs

| # | Gravité | Constat | Où |
|---|---|---|---|
| C1 | F | Le prix du kWh n'est pas conservé : une estimation relue remet `0,25` en dur ; si l'on recalcule avec une imprimante dotée d'une puissance, l'électricité change sans que la fiche l'ait demandé. | `MainWindow.Estimates.cs:181` |
| C2 | F | Coût machine = `heures × taux` non arrondi à l'écran, alors que `PrinterCostCalculator.MachineCost` existe et arrondit ; la matière n'est pas arrondie non plus. Les arrondis ne sont faits qu'au total. Écart de quelques centimes possible entre récapitulatif et somme des lignes. | `MainWindow.Estimates.cs:383,387` |
| C3 | i | La « marge » est une majoration sur coût (déjà relevée en R6) : 30 % de marge donne 23 % de marge commerciale. À préciser dans l'interface. | `EstimateCalculator.cs:24` |

### Détails à surveiller

- `StartEstimateFromModel` associe un modèle à un projet par `StartsWith(project.ProjectPath)` sans séparateur final : à corriger par comparaison de dossier parent.
- `ClientCodeChanger.Apply` renomme dossiers, fiches, réservations et estimations avec sa propre pile d'annulation, sans le journal d'opération d'`EntityManagementService` : une coupure brutale en plein changement de code n'est pas reprise au démarrage (le point est déjà cité comme limite le 8 octobre).
- Les projets sans fiche sont triés à `DateTimeOffset.MinValue` dans « Récents » : ils n'apparaissent jamais sur l'Accueil.

## Ce qui tient bien entre les pages

- Renommer, déplacer ou mettre à la corbeille un client ou un projet emmène ses fiches (clients, projets, collections) avec journal d'opération, contrôle de version et reprise (`EntityManagementService`).
- Une référence de projet n'est jamais redonnée (réservations, historique, corbeille).
- Les fichiers d'un projet passent par `ManagedFileService` ou `EntityManagementService` selon qu'il s'agit d'un fichier ou d'une racine d'entité : aucun contournement trouvé.
- La création de projet depuis Projets, Fiche client et Accueil suit le même chemin (`CreateTracked`).
- Les captures du visualiseur sont rangées dans le dossier photos du projet du modèle, sinon demandées.

## Ordre de traitement conseillé

1. **B1** : correctif d'une ligne + tests ; rend deux boutons à nouveau utilisables.
2. **B2 + B3** : corrigent la perte silencieuse de données dans les estimations ; à traiter ensemble (même écran, même fiche).
3. **B4** : lien par identifiant (migration douce : conserver la référence texte en lecture).
4. **B5 + B6 + B7** : décision à prendre sur le registre des codes, puis unicité des codes dans le Core.
5. **B8, B9, B10** : cohérence d'affichage.
6. Le reste au fil de l'eau.

Chaque correctif doit s'accompagner d'un test de non-régression sur espace temporaire, comme pour les corrections du 8 octobre.
