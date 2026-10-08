# Corrections de l’audit métier

**Objectif :** corriger les onze anomalies confirmées dans l’audit du 7 octobre et remplacer l’exécutable livré.

**Architecture :** conserver les identifiants des fiches, contrôler leur rattachement au chemin courant, appliquer la politique commune de sécurité des chemins et journaliser les renommages interrompus. Refuser les éditions obsolètes avant écriture et conserver les créations échouées dans la corbeille.

## Lots de correction

- [x] M01, M04, M05, M06 : régressions d’identité, homonymes, classement et codes avec underscore ; catalogues et sélection par identité/chemin ; tests ciblés initialement verts.
- [x] M02, M08, M09 : régressions avec jonctions et interruption simulée ; sécuriser transfert et navigateur, récupérer renommages de casse.
- [x] M03 : régressions réservation client, erreur registre, erreur fiche projet ; vérifier avant création, préserver toute création échouée sans déplacer de fiche préexistante.
- [x] M07, M10, M11 : régressions documents propres des dossiers legacy, édition concurrente et précision décimale ; contrôle conditionnel atomique des stores et conservation des brouillons.
- [x] Relire les changements entre lots, puis exécuter toute la suite et une compilation/publication Release : 403/403 tests et publication réussie.
- [x] Mettre à jour le suivi d’audit avec preuves et limites ; sauvegarder l’ancien binaire dans outputs/backups, remplacer MS3DPRINT Manager.exe, vérifier SHA256 et unicité.

## Vérification

Les tests utilisent exclusivement des fixtures temporaires. Chaque correction doit faire échouer sa régression avant modification et la faire réussir ensuite. Les builds sont sérialisés et leurs binlogs conservés dans outputs/logs. Aucun lancement supplémentaire de l’application ni modification des documents réels.

La politique Cloud Files accepte les placeholders Windows identifiés ; les jonctions et liens réels restent refusés. Le verrouillage des profils couvre les processus locaux, sans prétendre fournir une transaction distribuée entre machines synchronisées par Nextcloud. La récupération des créations couvre les erreurs prises en charge ; une coupure brutale pendant une création complète reste distincte des journaux de renommage.
