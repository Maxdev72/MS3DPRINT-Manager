# Audit complet — MS3DPRINT Manager

Date : 13 octobre 2026. Dépôt `sources/` au commit 7747484. Audit en lecture seule : aucun fichier de code, de données ou de livraison n'a été modifié.

## Périmètre et méthode

Vérifié : compilation et suite de tests complète, dépendances NuGet (vulnérabilités et mises à jour), empreintes et métadonnées de l'exécutable livré, relecture du code de `Core` (gestion de fichiers, corbeille, registre des codes, découpeur, stores JSON, estimations), de la couche Windows (impression PDF), du démarrage de l'application, du chargeur de maillages, des vignettes et de l'ouverture des fichiers.

Non vérifié : le logiciel n'a pas été lancé (pas de test visuel ni d'accessibilité interactif), `ModelViewerView.cs` (1137 lignes) et `GpuMeshViewport.cs` n'ont pas été relus en entier, la couverture de tests n'a pas été mesurée.

## Ce qui est solide

- 433 tests réussis, 0 échec ; `TreatWarningsAsErrors` actif, compilation propre.
- Aucune vulnérabilité NuGet connue, directe ou transitive.
- Livraison conforme à `AGENTS.md` : `MS3DPRINT Manager.exe` livré et binaire publié ont le même SHA256 (`e078cc1e…`), sauvegardes isolées dans `outputs/backups/`.
- Protection des chemins exemplaire : refus des liens, jonctions et points de réanalyse (sauf placeholders cloud), noms réservés Windows, métadonnées et dossiers structurels protégés, jamais d'écrasement.
- Opérations fichiers journalisées et récupérables (corbeille avec manifeste, renommage de casse avec journal de reprise), écritures atomiques (`.tmp` puis `Move`), verrous inter-processus sur les profils.
- Gestionnaire d'erreurs global : une erreur inattendue s'affiche et est consignée sans fermer le logiciel.
- Respect du réglage Windows « animations » ; répartition de `MainWindow` en fichiers partiels par page.

## Constats

Gravité : **M** moyenne, **F** faible, **i** information.

### Sécurité

| # | Gravité | Constat | Où | Recommandation |
|---|---|---|---|---|
| S1 | **M** | Un double clic sur un fichier non prévisualisable le lance via le shell, sans liste de blocage. Les dossiers projet contiennent des fichiers reçus de clients : un `.exe`, `.bat`, `.cmd`, `.ps1`, `.lnk`, `.js`, `.vbs`, `.hta`, `.msi`, `.scr` s'exécuterait. | `MainWindow.ProjectFiles.cs:284-289`, `Program.cs:19` (« Ouvrir avec… ») | Refuser ou confirmer l'ouverture des extensions exécutables, ouvrir leur dossier à la place. |
| S2 | F | L'exécutable n'est pas signé (Authenticode) : avertissement SmartScreen à chaque nouvelle version copiée. | `MS3DPRINT Manager.exe` | Signer, ou documenter l'avertissement. |
| S3 | F | Les noms et adresses saisis dans la recherche d'entreprise partent vers `recherche-entreprises.api.gouv.fr` et `data.geopf.fr` (HTTPS, requêtes encodées). C'est documenté dans le README et acceptable ; le délai d'attente du `HttpClient` n'a pas été vérifié. | `PublicClientLookup.cs` | Confirmer un délai court et l'absence d'envoi automatique en frappe sans temporisation. |
| S4 | F | Le découpeur est lancé sans délai maximum : seule l'annulation l'arrête. Les arguments passent bien par `ArgumentList` (pas d'injection). | `Slicer.cs:153-175` | Ajouter un délai (ex. 5 min). |
| S5 | i | Mises à jour disponibles : Avalonia 12.1.3 → 12.1.4, System.Drawing.Common 10.0.0 → 10.0.12 (correctifs). Paquets de test anciens (xunit 2.5.3, Test.Sdk 17.8). | `*.csproj` | Mettre à jour, puis relancer la suite. |

### Robustesse et intégrité des données

| # | Gravité | Constat | Où | Recommandation |
|---|---|---|---|---|
| R1 | **M** | `EstimateStore.LoadAll` échoue en entier si un seul JSON est corrompu. Même comportement pour les filaments, imprimantes, collections et projets, alors que `ClientProfileStore` a déjà `LoadReadable`. `ClientCodeChanger` appelle `LoadAll` des estimations : une estimation illisible bloque le changement de code client. | `EstimateCalculator.cs:77`, `FilamentStore.cs:19`, `ClientCodeChanger.cs:53,85` | Généraliser le modèle `LoadReadable` (liste + erreurs signalées) aux autres stores. |
| R2 | **M** | `EstimateStore` n'utilise pas `ProfileMutationLock` ni de contrôle `expectedUpdatedAt`, contrairement aux autres stores : deux instances ouvertes (ou Nextcloud en synchronisation) peuvent écraser une estimation. | `EstimateCalculator.cs:39-92` | Aligner sur `FilamentStore`/`PrinterStore`. |
| R3 | F | `ClientCodeRegistry.Add` crée le client (`createClient()`) avant d'écrire le registre : si l'écriture échoue, le client existe sans code enregistré. Le `WaitOne()` du mutex n'a pas de délai. Les sauvegardes `client-codes.previous.*.json` ne sont jamais purgées (7 fichiers dans `data/`). | `ClientCodeRegistry.cs:34-92` | Écrire le registre d'abord puis créer, ou annuler le client en cas d'échec ; purger au-delà de N sauvegardes ; délai sur le mutex. |
| R4 | F | Le visualiseur charge tout fichier STL/OBJ sans limite de taille (`ReadAllBytes`, STL ASCII ×3 en mémoire) ; seules les vignettes sont limitées à 40 Mo. Le glisser-déposer n'attrape que `ArgumentException`, `NotSupportedException`, `FileNotFoundException` : un `FormatException` ou `InvalidDataException` tombe dans le gestionnaire global (message générique). | `MeshLoader.cs`, `ModelViewerView.cs:495-505` | Seuil d'avertissement (ex. 500 Mo), élargir le `catch` au message utilisateur. |
| R5 | F | `ErrorLog` n'a pas de rotation (`erreurs.log` grossit sans limite). `SliceRunner.Read` ne libère pas son `JsonDocument`. | `ErrorLog.cs`, `Slicer.cs:182` | Tronquer au-delà de 1 Mo ; `using`. |
| R6 | F | `EstimateCalculator` ne contrôle pas les coûts négatifs (seuls échec, marge et remise sont bornés). Le prix de vente est le coût attendu × (1 + marge) : c'est un taux de marque, pas une marge commerciale ; le libellé « Marge » peut surprendre. | `EstimateCalculator.cs:14-24` | Valider ≥ 0 ; préciser « Majoration sur coût » ou documenter. |

### Performance

| # | Gravité | Constat | Recommandation |
|---|---|---|---|
| P1 | **M** | Le logiciel attend toujours 5 secondes au démarrage, même si tout est chargé (`SplashDurationMs = 5000`, boucle `Task.Delay`). | À décider : supprimer, ou réduire à ce qui est nécessaire (fermer dès que le préchargement est fini, minimum ~800 ms). `App.axaml.cs:9,41-47` |
| P2 | F | `CheckTree` appelle `EnsureNoLinks` sur chaque entrée, et celle-ci remonte toute la chaîne des parents (`File.GetAttributes` à chaque niveau) : coût en entrées × profondeur à la mise à la corbeille, à la restauration et à l'import d'un gros projet. | Vérifier les liens une fois par dossier en descendant, pas depuis la racine à chaque entrée. |
| P3 | i | Tous les `LoadAll` relisent les JSON à chaque appel, la recherche parcourt le disque à la demande. Sans effet visible à l'échelle d'un atelier (quelques centaines de fiches). | Surveiller si le volume croît. |
| P4 | i | Exécutable de 124 Mo (autonome, non réduit, ANGLE/Skia inclus). Pas de problème fonctionnel ; chaque livraison ajoute 124 Mo de sauvegarde. | Garder les 2-3 dernières sauvegardes seulement. |

### Architecture et maintenabilité

| # | Gravité | Constat | Recommandation |
|---|---|---|---|
| A1 | **M** | `Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot` est répété environ 55 fois dans les fichiers `MainWindow.*.cs`, avec le chemin par défaut codé en dur (`C:\MS3DPRINT\Nextcloud\MS3DPRINT`). Aucun réglage utilisateur ne permet de changer l'espace de travail (la page Paramètres l'affiche en lecture seule). | Une propriété `WorkspaceRoot` unique, lue depuis `settings.json` avec la variable d'environnement en surcharge ; ajouter le choix dans Paramètres. |
| A2 | F | Les variables de démonstration `MS3DPRINT_START_PAGE/PROJECT/CLIENT/DIALOG/MODEL/CUT/COMPARE` sont compilées dans la version livrée. Pas de risque direct, mais de la surface et du code mort en production. | Les regrouper dans une classe `DemoHooks`, éventuellement sous `#if DEBUG`. |
| A3 | F | `ModelViewerView.cs` (1137 lignes) et `GpuMeshViewport.cs` (505) concentrent beaucoup de responsabilités ; la construction d'interface se fait en C# (`Notify`, `LoadSettingsAsync`…) plutôt qu'en AXAML. | Extraire les panneaux (Infos, Affichage, Outils, Analyse) en contrôles séparés. |
| A4 | F | Environ 60 gestionnaires `async void` avec filtres d'exception étroits ; le filet global les rattrape, mais certains messages seront génériques. `DashboardCreateClient_Click` est `async void` sans `await`. | Acceptable pour des gestionnaires d'événements ; retirer `async` inutile. |
| A5 | F | `CultureInfo.GetCultureInfo("fr-FR")` est répété une vingtaine de fois. | Constante partagée. |

### Livraison, dépôt et processus

| # | Gravité | Constat | Recommandation |
|---|---|---|---|
| L1 | **M** | Le dépôt Git, `bin/`, `obj/` et `outputs/` (1,5 Go) sont dans un dossier synchronisé par Nextcloud. Les verrous Git, l'index et les fichiers de build en cours d'écriture peuvent être synchronisés au mauvais moment (conflits, corruption). | Exclure `.git`, `bin`, `obj`, `outputs` de la synchronisation (`.sync-exclude.lst`) ou déplacer le dépôt hors Nextcloud. |
| L2 | F | Le dossier de livraison contient en plus de l'exécutable deux `.pdb` obsolètes du 29 septembre (`MS3DPRINT.Manager.App.pdb`, `…Core.pdb`). `outputs/MS3DPRINT-Manager/` garde un ancien `MS3DPRINT.Manager.App.exe` (hash différent) qui peut être pris pour le bon binaire. | Supprimer ces résidus (sur accord) ; vider le dossier de publication avant chaque publication. |
| L3 | F | La version interne de l'exécutable est `1.0.0+644d6c8`, alors que le dernier commit est 7747484 : soit le dernier commit a été fait après la compilation, soit l'exécutable livré ne contient pas ses changements (accessibilité des onglets, purge des vignettes). | Vérifier en relançant la publication puis en comparant les hash ; le journal des modifications est la seule trace de version. |
| L4 | F | Pas d'intégration continue, pas de mesure de couverture (`coverlet` est installé mais inutilisé), pas d'analyseurs ni de contrôle de format. | Un workflow minimal : `dotnet test` avec couverture, `dotnet format --verify-no-changes`. |
| L5 | F | Les documents `docs/audits/2026-10-08-pages-ux-crud.md` et plusieurs plans citent WPF, `MainWindow.xaml.cs`, `ProjectDetailView` : obsolètes depuis la migration Avalonia. | Ajouter un bandeau « historique, antérieur à la migration Avalonia ». |

### Accessibilité et ergonomie (lecture du code et du journal uniquement)

Points positifs : noms d'automatisation sur les contrôles principaux (92 occurrences dans `MainWindow.axaml`), état des onglets du visualiseur annoncé, cadre de focus sur la vue 3D, boutons de fermeture des notifications, raccourcis `Ctrl+1…9`, `Ctrl+K`, `Ctrl+N`, quatre thèmes avec contrôle de contraste testé. Pistes : la vue 3D n'est utilisable qu'à la souris pour la rotation (alternative clavier non vérifiée) ; les notifications d'erreur disparaissent après 8 s (un lecteur d'écran peut les manquer, la région live n'a pas été vérifiée). À valider en lançant le logiciel.

## Ordre de traitement conseillé

1. **S1** — bloquer ou confirmer l'ouverture des fichiers exécutables (petit changement, gain de sécurité réel).
2. **R1 + R2** — tolérer un JSON illisible et verrouiller `EstimateStore`.
3. **A1** — centraliser l'espace de travail et le rendre configurable.
4. **P1** — décider du sort de l'écran de démarrage de 5 s.
5. **L1** — sortir `.git`, `bin`, `obj`, `outputs` de la synchronisation Nextcloud.
6. **L2 + L3** — nettoyer la livraison et confirmer que l'exécutable correspond au dernier commit.
7. Le reste (R3-R6, P2, S3-S5, A2-A5, L4-L5) au fil de l'eau.
