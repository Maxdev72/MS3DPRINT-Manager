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
