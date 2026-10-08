# Audit de logique métier — MS3DPRINT Manager

Suivi : les onze constats confirmés ont été corrigés dans la [livraison du 8 octobre 2026](2026-10-08-corrections-metier.md). Ce document conserve l’état et les preuves de l’audit initial.

Audit du 7 octobre 2026, version `01f565b4af72b1d73261a02d3f1b43084b859851`, branche `feat/full-management-filaments`.

La gestion courante est couverte par les tests, mais certains parcours ne partagent pas les mêmes règles d’identité, de classement et de sécurité des chemins. **11 anomalies confirmées : 2 P1, 8 P2 et 1 P3.** Les deux P1 peuvent affecter des données et doivent être traitées avant d’étendre les fonctionnalités.

P1 : correction prioritaire, risque pour les données ou leur rattachement. P2 : parcours métier incohérent ou indisponible dans les conditions décrites. P3 : défaut limité de précision.

## Méthode et limites

- Lecture croisée des services Core, formulaires WPF, navigation, catalogues, recherche et tests.
- Suite existante : **364 réussites, 0 échec, 0 test ignoré**. Ce résultat ne couvre pas les scénarios nouveaux ci-dessous.
- Reproductions supplémentaires sur fixtures isolées sous le dossier temporaire Windows. Aucune opération sur les documents utilisateur.
- Le scénario de renommage interrompu simule l’état persistant après le premier mouvement ; aucun crash réel n’a été provoqué.
- La mauvaise présélection client est vérifiée avec le prédicat exact du formulaire, après mouvements et renommages publics, sans soumettre le formulaire modal.
- La précision filament est vérifiée par le formulaire non affiché et son callback réel de sauvegarde. La tentative de fermeture modale intervient après la persistance ; elle ne participe pas au constat.
- Audit de cohérence applicative, sans campagne de charge, sans synchronisation simultanée sur deux machines ni validation exhaustive de tous les pilotes Cloud.
- Aucun correctif de production ni remplacement d’exécutable effectué pendant cet audit.

Preuves : [résultats des reproductions](2026-10-07-audit-metier-preuves.json). Le runner local est sous `outputs/business-audit/`, ignoré par Git. La lecture des fichiers source du dépôt a servi au contrôle de recherche ; aucun document métier réel n’a été interrogé.

## Constats prioritaires

### M01 — P1 — La suppression d’une copie de projet emporte la fiche du projet d’origine

Source : [ProjectCatalog.cs:47](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.Core/Projects/ProjectCatalog.cs:47), [EntityManagementService.cs:75](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.Core/Workspace/EntityManagementService.cs:75).

Créer un projet suivi chez A, puis un dossier de même nom chez B, sans fiche. Le catalogue attribue le même identifiant de fiche aux deux dossiers, car il associe les JSON par nom de dossier. Supprimer celui de B vers la corbeille déplace aussi le JSON du projet A. Les documents A restent présents, mais sa fiche disparaît du catalogue actif.

Preuve `PROJECT-DUPLICATE-PROFILE` : deux résumés partagent un ID ; après suppression de B, nombre de profils actifs = 0 et projet A sans fiche.

Correction : associer une fiche au chemin enregistré et à l’identité du client ; traiter les dossiers copiés comme éléments sans fiche. Vérifier cette association avant de déplacer des métadonnées liées. Tester également renommage, déplacement et restauration de la copie.

### M02 — P1 — « Classer » déplace un fichier hors de l’espace géré à travers une jonction

Source : [ProjectFileTransferService.cs:30](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.Core/Files/ProjectFileTransferService.cs:30).

Remplacer `02_FICHIERS_CLIENT` d’un projet par une jonction vers un dossier extérieur. Le service vérifie que le chemin semble appartenir au projet et qu’il désigne un dossier ; il ne vérifie pas la redirection physique. Le déplacement réussit et retire le fichier de sa source pour l’écrire à l’extérieur.

Preuve `F1` : source absente, document extérieur présent avec contenu intact. Le risque concerne l’emplacement réel du document, pas un écrasement observé.

Correction : appliquer la politique commune `WorkspacePathSafety` à la source, au projet, aux ancêtres et à la destination du transfert, en conservant l’exception prévue pour les placeholders Cloud Files. Tester liens, jonctions, collisions et destinations ordinaires.

## Autres anomalies confirmées

### M03 — P2 — Une création client refusée laisse une nouvelle fiche et un nouveau dossier

Source : [ClientCreationService.cs:26](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.Core/Clients/ClientCreationService.cs:26).

Créer ACME, le mettre à la corbeille, puis recréer un client nommé ACME. Le dossier et le JSON sont écrits avant l’ajout au registre des codes. La clé historique ACME provoque un refus tardif, mais la nouvelle fiche reste active. La restauration du client original rencontre alors ce dossier concurrent.

Preuve `CLIENT-RECREATE` : exception de création, nouvelle fiche pourtant persistée, registre encore associé à l’ancien code et restauration refusée. Même frontière d’atomicité à examiner pour la création d’un projet, qui crée le dossier avant son JSON.

Correction : décider explicitement comment réserver les anciens noms/codes, prévalider le registre et rendre la création récupérable ou annulable comme une seule opération. Une création annoncée en échec ne doit pas laisser un nouvel élément actif.

### M04 — P2 — La création d’un projet peut présélectionner le mauvais client

Source : [CreateTrackedProjectWindow.xaml.cs:69](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.App/Views/CreateTrackedProjectWindow.xaml.cs:69).

Déplacer A sous `01_CLIENTS/GROUP`, puis renommer le dossier de B en A. Les deux clients distincts ont maintenant le même nom de dossier. La création depuis la fiche B sélectionne le premier A, au lieu de rechercher l’identifiant de B. La complétion d’un projet historique filtre également par nom de dossier.

Preuve `CLIENT-PRESELECT-FOLDERNAME` : ID attendu et ID sélectionné différents après opérations autorisées.

Correction : présélectionner par `ClientProfile.Id` et vérifier le chemin pour les éléments historiques ; afficher une information permettant de distinguer les homonymes.

### M05 — P2 — Les projets d’un client classé disparaissent de « Classer un fichier »

Source : [ClassifyFileViewModel.cs:113](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.App/ViewModels/ClassifyFileViewModel.cs:113).

Après déplacement d’un client sous un dossier de classement, le catalogue des projets retrouve sa fiche grâce au chemin enregistré. Le sélecteur de classement recherche encore exactement deux niveaux : client puis projet. Il ne retrouve plus le projet classé, même lorsque son chemin est transmis depuis sa fiche.

Preuve `F3` : un projet dans le catalogue après déplacement, zéro choix dans Classer, contre un choix avant déplacement.

Correction : alimenter le sélecteur à partir du catalogue métier commun et de ses chemins validés.

### M06 — P2 — Certains codes acceptés créent des projets historiques invisibles

Sources : [ProjectCatalog.cs:9](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.Core/Projects/ProjectCatalog.cs:9), [ClientCatalog.cs:9](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.Core/Clients/ClientCatalog.cs:9).

Le générateur accepte le code normalisé `ACME_FR` et crée `ACME_FR-2026-001_TEST`. Les expressions de reconnaissance des catalogues acceptent seulement lettres et chiffres dans le code. Le projet sans fiche devient invisible et le compteur client vaut zéro. Un projet suivi disposant d’un chemin indexé peut être retrouvé par l’autre branche du catalogue.

Preuve `LEGACY-CODE-SEPARATOR` : dossier créé présent, zéro projet visible et compteur zéro.

Correction : une seule définition du format de référence, partagée entre génération, catalogues, classification et récupération des références.

### M07 — P2 — Des fournisseurs historiques disparaissent alors que leurs documents restent présents

Source : [CollectionCatalog.cs:26](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.Core/Collections/CollectionCatalog.cs:26).

Un fournisseur sans fiche possède `LEGACY/02_TARIFS/tarif.pdf`. Ajouter une fiche indexée sous `LEGACY/MOVED` fait masquer LEGACY comme dossier de classement. Le filtre vérifie uniquement les fichiers directement dans LEGACY et ignore ses propres documents dans les sous-dossiers. Le même code sert aux modèles et produits.

Preuve `COLLECTION-LEGACY-SUBDOCUMENTS` : fournisseur parent visible avant, absent après ; tarif intact et élément imbriqué visible.

Correction : distinguer explicitement un dossier de classement d’un dossier métier historique. Ne pas déduire cette distinction de la seule présence de fichiers directs.

### M08 — P2 — Le navigateur de documents peut consulter un dossier extérieur lié

Source : [ProjectFileBrowser.cs:12](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.Core/Files/ProjectFileBrowser.cs:12).

Une jonction sous une fiche apparaît comme dossier. Le navigateur contrôle le préfixe du chemin, puis suit la jonction et énumère des documents extérieurs. Le chemin donné aux fonctions d’ouverture permet de lire leur contenu.

Preuve `F2` : dossier lié affiché, fichier extérieur listé et contenu lisible. Les contrôles des mutations ordinaires ne couvrent pas cette consultation.

Correction : appliquer la politique commune de chemins au navigateur et à l’ouverture, avec les mêmes exceptions Cloud Files que les autres services.

### M09 — P2 — Un renommage de casse interrompu bloque la récupération automatique

Sources : [ManagedFileService.cs:39](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.Core/Workspace/ManagedFileService.cs:39), [EntityManagementService.cs:245](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.Core/Workspace/EntityManagementService.cs:245).

Le renommage `ACME` vers `acme` passe par `.rename-{Guid}`. Cet emplacement intermédiaire n’est pas enregistré dans le journal. Une interruption après le premier déplacement laisse les documents à cet emplacement ; la récupération connaît uniquement ACME et acme et échoue. Les opérations qui exigent une récupération préalable restent bloquées.

Preuve `F4` : simulation de cet état persistant ; document conservé dans l’intermédiaire, journal toujours en attente et récupération refusée. Aucune perte physique de document constatée.

Correction : enregistrer l’intermédiaire avant le premier déplacement et prévoir chaque étape de reprise, y compris pour les fichiers ordinaires.

### M10 — P2 — Une sauvegarde ancienne écrase une modification plus récente

Sources : [ClientDetailViewModel.cs:74](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.App/ViewModels/ClientDetailViewModel.cs:74), [ClientProfileStore.cs:56](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.Core/Clients/ClientProfileStore.cs:56).

Ouvrir deux instantanés du même client. Le premier change son nom et sauvegarde. Le second change seulement une note puis sauvegarde sa copie complète : le nom revient silencieusement à sa valeur initiale. Ce cas peut correspondre à deux instances ou à une modification externe pendant qu’une fiche reste ouverte. Le rafraîchissement des projets/documents ne met pas à jour la version du profil édité.

Preuve `STALE-EDIT` : nom initial rétabli et nouvelle note enregistrée. L’historique peut conserver la version précédente, mais aucun conflit n’est présenté à l’utilisateur.

Correction : contrôler une version de profil avant remplacement et proposer un rechargement ou une résolution de conflit. Étendre la même règle aux autres stores ; un simple verrou local ne résout pas les conflits entre machines synchronisées.

### M11 — P3 — Modifier un filament arrondit un prix sans modification du champ prix

Source : [FilamentEditorWindow.xaml.cs:29](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.App/Views/FilamentEditorWindow.xaml.cs:29).

Créer un prix à `24,991 €/kg`, rouvrir la fiche et modifier seulement le nom. Le formulaire affiche deux décimales puis sauvegarde `24,99`. Le parseur et le store acceptent pourtant une précision supérieure.

Preuve `FILAMENT-PRICE-ROUNDTRIP` : prix persistant passé de 24,991 à 24,99 après modification du nom seul.

Correction : préserver la précision enregistrée, ou définir et appliquer une règle d’arrondi explicite dès la création. Ne pas modifier implicitement une valeur inchangée lors de l’édition.

## Point conditionnel non retenu parmi les 11 confirmations

[GlobalSearchService.cs:97](C:/MS3DPRINT/Nextcloud/MS3DPRINT/98_DEVELOPPEMENT/MS3DPRINT_MANAGER/sources/src/MS3DPRINT.Manager.Core/Search/GlobalSearchService.cs:97) exclut tous les dossiers portant `ReparsePoint`, tandis que `WorkspacePathSafety` autorise certains placeholders Cloud Files. Un dossier conservant ce drapeau peut donc être omis de la recherche.

Le contrôle en lecture seule sur les sources du dépôt a retrouvé le fichier recherché ; le dossier vérifié ne portait plus ce drapeau lors de l’exécution. **Ce point reste une analyse conditionnelle du code, sans reproduction Cloud contrôlée.** Prévoir une validation dédiée aux états en ligne/hors ligne du fournisseur de synchronisation.

## Couverture métier et limites fonctionnelles

| Domaine | Disponible | Fragilité principale |
|---|---|---|
| Clients | Fiches, contacts, classement, corbeille | Identité encore confondue avec nom de dossier dans certains parcours ; création non atomique |
| Projets | Références réservées, rattachement, statut, échéance, documents | Association des fiches et sélecteurs incohérente dans les cas copiés/classés |
| Fournisseurs/modèles/produits | Fiches et documents, adoption historique, déplacements | Dossier de classement et dossier métier insuffisamment distingués |
| Fichiers | Import par copie, classement par déplacement, renommage, ouverture | Politique de chemins différente entre parcours |
| Corbeille | Conservation des documents/profils et restauration sans écrasement | Dépend de l’association correcte des fiches ; récupération des renommages de casse incomplète |
| Filaments | Bibliothèque, prix/kg, matière, abrasivité, duplication et filtres | Précision de prix à stabiliser ; pas de consommation par projet |
| Recherche/tableau de bord | Recherche à la demande et compteurs de statuts | Héritent des erreurs des catalogues ; états Cloud à vérifier |

Les filaments ne sont pas encore reliés à des consommations, des masses ou des coûts de projet. Les devis et factures sont des dossiers documentaires et un statut projet ; il n’existe pas de moteur de chiffrage ou de facturation dans le périmètre examiné. Les quantités, temps de fabrication et stocks ne sont pas des entités métier de cette version. Ces absences sont des évolutions possibles, distinctes des anomalies ci-dessus ; la gestion de stock et la purge définitive avaient notamment été exclues de la livraison.

## Ordre de correction recommandé

1. M01/M04 : rattachement par identifiant et chemin validé, plus tests de copies et d’homonymes.
2. M02/M08 : politique commune de chemins dans transfert, navigation et ouverture.
3. M03/M09 : opérations récupérables de bout en bout pour création et renommage, y compris leurs étapes intermédiaires.
4. M05/M06/M07 : catalogues métier communs, format de référence unique, distinction explicite des dossiers de classement.
5. M10/M11 : contrôle de version et conservation des valeurs métier ; puis validation des états Cloud.

Avant une extension fonctionnelle, intégrer ces reproductions en tests de non-régression et refaire un cycle complet sur données de démonstration : création, import, renommage, classement, suppression et restauration. L’audit fournit le diagnostic et les critères de correction ; les corrections restent à réaliser.
