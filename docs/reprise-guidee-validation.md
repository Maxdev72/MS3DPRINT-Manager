# Validation de la reprise guidée — 6 novembre 2026

- 1 299 tests Core/Windows réussis : outputs/guided-workspace-reconciliation/core-final.log.
- 124 tests serveur réussis sous Windows et Ubuntu (WSL), dont cinq parcours natif/serveur
  de transfert, perte de réponse, reprise après fermeture et refus terminal avant activation.
- 8 tests Linux réussis, y compris distinction des ateliers PC/pc et permissions privées.
- Captures natives du bilan en largeur 1 400 et 960 : outputs/guided-workspace-migration/.
- Publication autonome Windows et Linux réussie ; publication serveur Linux préparée dans
  outputs/guided-workspace-reconciliation/server-candidate/.

Les scénarios vérifient les empreintes, les annexes choisies, les versions métier et techniques,
la conservation des sources et fichiers serveur seuls, les limites et protections d’accès,
les commandes Pending, les coupures réseau et les frontières de redémarrage de la bascule.
Les erreurs de persistance bloquent l’accès métier jusqu’à récupération vérifiée. Une reprise
refusée avant activation est terminale ; une nouvelle préparation garde les anciens journaux.

Les revues ont conduit à corriger la persistance du reçu, la récupération après restauration,
la publication mobile pendant la reprise, les callbacks de contrôleurs fermés et le parcours
après refus. Ces régressions ont été reproduites avant correction et vérifiées après.

Les premières exécutions complètes Core ont signalé trois attentes d’anciens tests de réglages
à adapter à la nouvelle carte. Après correction, la suite complète ci-dessus passe sans échec.
L’avertissement Avalonia AVA3001 des tests utilisant ses API internes est préexistant.

Aucun atelier réel n’a été transféré ou activé. Aucun service Coolify n’a été modifié. Le serveur
public conserve sa version précédente tant qu’une mise à jour explicite n’a pas été réalisée.
Les sites commerciaux restent hors périmètre. L’APK Android n’est pas remplacé.

L’exécutable utilisateur unique MS3DPRINT Manager.exe a été remplacé après fermeture
normale. La version précédente reste dans outputs/backups/. SHA256 publié/livré identique :
178547A1B0BF9E4331081A69AA7615BDD7AFEADB778214962A689420940B6F62.
Reçu : outputs/guided-workspace-reconciliation/delivery.json. Source métier : 29eb80f.

L’image locale ms3dforge-compagnon:29eb80f est construite :
sha256:47590219e884f52aa48d88af004024c6e29e6705aae1483e1e52eb0476b7c42c.
Son démarrage sous l’utilisateur app a répondu santé HTTP200, sans publication d’un port
sur l’hôte, puis le conteneur de test éphémère a été arrêté. Le stage de vérification passe
ses 124 tests dans Docker. La restauration réseau de ce stage échouait initialement sur
la chaîne de certificats NuGet du moteur local ; le cache local des paquets vérifiés a été
fourni dans un contexte de build séparé, hors Git. La validation TLS n’a pas été désactivée.
Preuves : docker-image.log, docker-runtime-health.log et docker-verify-offline.log.

La version Linux est également reconstruite, ses 8 tests repassent et sa fenêtre native
s’ouvre sous Ubuntu avec un atelier fictif. L’archive remplace la précédente dans outputs/.

## Mise à jour du compagnon en production

Le 6 novembre 2026, après accord de reprise de l’utilisateur, Coolify a déployé la version
29eb80feaa69d1bd6592c7c54b97c1548e946de9 du compagnon. Le déploiement est marqué Success.
Le volume persistant existant et le chemin d’atelier sont conservés. Une sauvegarde locale
complète du volume a réussi avant la mise à jour, avec arrêt temporaire du seul compagnon
pendant l’archive puis redémarrage. La sauvegarde planifiée créée pour cette opération reste
désactivée ; aucune récurrence n’a été laissée active.

Contrôles après mise à jour : santé HTTPS 200 ; nouvelle route de capacités de reprise 401
sans session ; clients 401 sans session. Les deux sites commerciaux répondent en HTTPS 200 ;
aucune modification de leurs ressources n’a été effectuée. Les contrôles publics confirment
la présence et la protection de l’API ; le parcours authentifié sur les données réelles reste
à effectuer depuis Manager par le propriétaire. Aucune nouvelle reprise métier n’a été lancée.

Le reçu opérationnel détaillé de sauvegarde et déploiement reste hors Git dans
outputs/guided-workspace-reconciliation/production-update.json. La partie développement
ci-dessus décrit l’état avant ce déploiement ; les limites Docker et tests locaux y restent
les preuves de préparation de cette même version.

## Correctif des anciens chemins Windows

Le 6 novembre 2026, un ancien chemin de collection utilisant une barre oblique inverse
provoquait « Chemin invalide dans la sauvegarde » dès la capture préalable. La capture
signale maintenant cette fiche comme bloquante sans empêcher sa sauvegarde brute.
La préparation portable conserve ses contrôles : un dossier manquant exige toujours
un choix explicite de mise en annexe, et les octets de la fiche source sont préservés.

Le test reproduisant le blocage a échoué avant le correctif puis passe après correction.
Validation : 125 tests serveur sous Windows, 125 sous Ubuntu, 26 tests natifs ciblés,
et publication Windows réussie. Journaux locaux : legacy-path-green.log,
linux-server-tests.log, legacy-native.log et legacy-path-publish.log dans
outputs/guided-workspace-reconciliation/.

Après fermeture normale de Manager, remplacement de l’exécutable utilisateur unique,
avec sauvegarde de la version précédente dans outputs/backups/. SHA256 publié/livré :
B718C4EC33E1641DD88DD6564175462ED067299E3B1D7F749B74AD989419E1DE.
Reçu : outputs/guided-workspace-reconciliation/legacy-path-fix-delivery.json.
Aucune donnée métier réelle modifiée ni importée pendant ce correctif. Le serveur
reste sur son déploiement précédent ; aucun redéploiement nécessaire pour ce blocage
de préparation sur le PC. Le binaire Linux livré précédemment reste inchangé.

## Premier envoi simplifié — 6 novembre 2026

Le panneau natif enchaîne maintenant connexion, préparation et comparaison. Une seule
confirmation finale est nécessaire sans conflit ; les conflits peuvent être réglés par
un choix explicite groupé pour le PC ou individuellement. Les anciennes fiches réellement
sans dossier sont conservées dans une annexe privée, signalée au bilan final. Les entrées
hors périmètre restent indiquées. Après reçu vérifié et source stable, le mode serveur
s’ouvre automatiquement. Aucune activation n’est envoyée par Entrée ni sans confirmation.

Le test de parcours reproduisait les clics intermédiaires avant changement (rouge), puis
passe après simplification. Validation finale : 1 302 tests Core Windows réussis et 8 tests
Linux réussis. Le premier passage complet a relevé une régression de recherche des
paramètres avec « sauvegarde », corrigée avant la passe complète finale. Les tests couvrent
les annexes automatiques, le choix groupé, le gel des choix, le reçu incertain, la dérive
serveur et le refus de bascule automatique si le PC change pendant l’activation.
L’avertissement Avalonia AVA3001 est préexistant. Le détecteur de surface ne signale rien ;
le bilan a été vérifié visuellement aux largeurs 960 et 1 400 avec des données fictives.

Publication Windows réussie. SHA256 du candidat interne :
C696BBED6CAA801B8189E96E1998B92A02D7959B4E7948CDB3B5761C597A926A.
L’archive Linux a été remplacée ; SHA256 du binaire :
CB6C8844A50AE3A22CF139022D4C474277A4D99E6CF8707FF888492C3DACCC5D.
Le remplacement de l’exécutable utilisateur Windows attend la fermeture normale de Manager,
encore ouvert lors de la préparation. Son état effectif est consigné hors Git dans
outputs/guided-workspace-reconciliation/simple-sync-delivery.json.

Aucune donnée réelle n’a été transférée ou activée, aucun service déployé. Le compagnon
existant reste compatible ; les sites commerciaux sont inchangés. Cette simplification
facilite la reprise initiale puis le travail sur le serveur pour clients, projets et tâches ;
elle ne synchronise pas encore en continu les fichiers modifiés séparément sur le disque.
Preuves : simple-sync-full-final.log, simple-sync-linux-tests.log, simple-sync-publish.log
et leurs journaux de compilation dans outputs/guided-workspace-reconciliation/.

Après confirmation de fermeture normale par l’utilisateur, l’exécutable utilisateur unique
MS3DPRINT Manager.exe a été remplacé par le candidat simplifié. L’empreinte publiée et
livrée est identique : C696BBED6CAA801B8189E96E1998B92A02D7959B4E7948CDB3B5761C597A926A.
La version précédente est sauvegardée uniquement dans outputs/backups/. Le reçu
simple-sync-delivery.json indique maintenant windowsReplaced=true. Aucun atelier réel
n’a été modifié ni transféré pendant ce remplacement ; Manager n’a pas été relancé.

## Dossiers historiques bloquant la comparaison — 6 novembre 2026

Un atelier serveur peut contenir des dossiers projet qui n’ont jamais eu de fiche.
L’ancienne comparaison les bloquait et le panneau proposait malgré tout des choix de
versions impossibles à enregistrer. Le correctif conserve ces seuls dossiers serveur
comme documents historiques explicitement signalés au bilan, sans création de fiches.
Les autres validations métier et les protections de bascule sont conservées.

Tests de reproduction rouges puis verts : préparation serveur refusée sur un dossier
historique ; choix encore affichés à tort pour une opération bloquée. Validation finale :
1 303 tests Core Windows, 130 tests serveur Windows, 130 tests serveur Ubuntu. Les tests
incluent la reprise sans renvoi des morceaux, la dérive des sources, un journal de bascule
existant et d’autres anomalies mêlées. L’audit brut reste bloquant pour un projet sans fiche.
Les archives et ateliers de tests sont fictifs. Publication Windows réussie, SHA256 :
FB813B9F2B59727DAEEC363AE53613DDF7E85C97C0B56010B2D08C66544B470C.

Le code est préparé pour une mise à jour du seul compagnon. Son déploiement et le
remplacement de l’exécutable utilisateur seront consignés après vérification effective.
Aucune activation de l’atelier réel n’est effectuée par ce correctif.
Preuves locales : historical-core-full.log, historical-server-final.log,
historical-ui-green.log, linux-server-tests.log et historical-publish.log.

Le correctif 137cba9923840f8e558443164ee315a874514435 est maintenant déployé sur le seul
compagnon : déploiement Coolify pdfpytfnjfw1csohlrvtlgag marqué Success, en 27 secondes.
Avant la mise à jour, une nouvelle sauvegarde complète du volume a réussi (disponibilité
locale, 3,83 Go), avec arrêt temporaire du compagnon pendant l’archive puis redémarrage.
Le planning reste désactivé. Le volume, la racine d’atelier et les accès existants sont conservés.
Le reçu opérationnel privé historical-folders-delivery.json consigne le chemin de l’archive.

L’exécutable Windows utilisateur a été remplacé après fermeture de Manager. Le SHA256
publié/livré est identique à celui indiqué ci-dessus. La version précédente est uniquement
dans outputs/backups/. L’archive Linux est reconstruite, ses 8 tests passent et son binaire
porte SHA256 DB042464DDAF5061FA4C1835466975245EA0DB3453C7654FC5CABA57C2351572.
L’image locale ms3dforge-compagnon:137cba9 est construite avec le Dockerfile de production.
Une vérification Docker additionnelle a été interrompue pendant la copie du cache NuGet
local volumineux, avant ses tests ; elle n’est pas comptée comme une validation réussie.
Les 130 tests du serveur passent effectivement sous Windows et Ubuntu, séparément.

Contrôles de production : santé HTTPS 200, capacités de reprise et clients 401 sans session.
Les deux sites commerciaux répondent 200 en HTTPS et aucune de leurs ressources n’a été
modifiée. Aucun atelier réel n’a été activé par l’agent ; le propriétaire doit rouvrir Manager
pour reprendre sa comparaison déjà envoyée, choisir ses versions et confirmer le bilan.
