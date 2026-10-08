# Corrections de l’audit métier — 8 octobre 2026

Les onze anomalies confirmées de l’[audit initial](2026-10-07-logique-metier.md) ont été corrigées. Les preuves de l’audit initial restent historiques ; elles décrivent la version antérieure aux corrections.

| Constat | Comportement corrigé | Régressions principales |
|---|---|---|
| M01 | Une fiche projet appartient au chemin courant et au client identifié. Une copie conserve le statut sans fiche et ne déplace pas le JSON original. | ProjectCatalogTests, EntityManagementServiceTests |
| M02 | Les transferts vérifient la source, le projet et les ancêtres de la destination ; les jonctions sont refusées avant mouvement. | PathLinkSafetyTests, ProjectFileTransferServiceTests |
| M03 | Le registre vérifie les noms réservés avant création sous mutex. Une erreur ultérieure place les éléments créés en corbeille ; les JSON sont créés par fichier temporaire puis déplacement sans écrasement. | ClientCreationServiceTests, ProjectCreationServiceTests |
| M04 | La sélection d’un client utilise son identifiant ou son chemin, avec libellé distinguant les homonymes. | CreateTrackedProjectWindowTests |
| M05 | Le classement utilise le catalogue partagé, incluant les clients déplacés dans des groupes. | ClassifyFileViewModelTests |
| M06 | Le format commun reconnaît les codes tels que ACME_FR dans les catalogues, références et suggestions de fichiers. | ProjectCatalogTests, ProjectReferenceGeneratorTests, ProjectFileClassifierTests |
| M07 | Les dossiers anciens restent visibles lorsqu’ils contiennent leurs propres documents en sous-dossiers, en excluant les documents des fiches classées descendantes. | CollectionCatalogTests |
| M08 | Le navigateur refuse de parcourir un lien et masque les enfants dangereux lors du listing. | PathLinkSafetyTests, ProjectFileBrowserTests |
| M09 | Le renommage de casse journalise son chemin intermédiaire avant mouvement. La reprise restaure le nom original, puis les fiches d’une opération non finalisée. | CaseRenameJournalTests |
| M10 | Les formulaires sauvegardent avec la version de leur instantané ; un conflit conserve le brouillon. Les stores et les déplacements associés partagent des mutex locaux, et les versions sont vérifiées avant mouvement. | ProfileConcurrencyTests, EntityRelocationConcurrencyTests, CollectionEditorWindowTests |
| M11 | Le formulaire filament conserve la précision du prix décimal lors d’une modification d’un autre champ. | FilamentViewsTests |

## Vérification

- Suite complète : **403 réussites, 0 échec, 0 test ignoré**.
- Publication Windows autonome : profil PortableWinX64, Release, fichier unique, réussie.
- Rendu de 36 vues/formulaires avec les ressources réelles dans les quatre thèmes ; inspection des formulaires filament gris et fournisseur papier.
- Exécutable livré remplacé : `MS3DPRINT Manager.exe`, seul exécutable à la racine. SHA256 publié et livré identique : `770C47A4558544C6B56E2895DC69086BC76825E2205CC07B8556D37BB2774AF4`. L’ancienne version est sauvegardée uniquement dans `outputs/backups/`.
- Binlogs conservés localement dans `outputs/logs/`, notamment `audit-fixes-full-green-*.binlog` et `audit-fixes-publish-*.binlog`.
- Régressions exclusivement sur fixtures temporaires : jonctions réelles, erreurs de persistance, collisions, deux écrivains locaux et interruptions de renommage simulées. Aucune opération sur les documents métier réels.

## Limites

Les mutex coordonnent les processus locaux ; ils ne constituent pas un verrou distribué entre deux machines synchronisées par Nextcloud. Une modification externe est détectée si elle change la version UpdatedAt de la fiche. L’API Update sans version reste disponible pour compatibilité ; les formulaires et mutations métier concernés utilisent la surcharge conditionnelle.

La récupération des créations couvre les erreurs prises en charge pendant l’appel. Une coupure brutale pendant une création complète n’est pas couverte par un journal de création. Les renommages de casse et déplacements de fiches disposent de leurs journaux propres. Un échec de récupération conserve les documents et signale le blocage.

Les placeholders Cloud Files identifiés restent autorisés par la politique commune. La piste conditionnelle concernant la recherche globale et certains pilotes Cloud n’a pas été reproduite ni corrigée dans cette livraison.
