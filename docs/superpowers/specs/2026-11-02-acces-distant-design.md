# Accès distant MS3DForge — conception

Date : 2 novembre 2026.

## Décisions validées

L'objectif est de retrouver les fonctions de MS3DPRINT Manager sur le web et
Android, y compris lorsque le PC est éteint. Le serveur devient la référence
des données ; le PC se synchronise avec lui. Le nom de domaine retenu est
`ms3dforge.fr`, avec hébergement Docker dans Coolify.

La première livraison fonctionnelle couvre la connexion, la migration d'un
atelier de test, les clients, les projets, les tâches et les fichiers sur les
trois supports. Les autres modules sont des étapes suivantes de l'objectif,
pas des fonctions réputées livrées par cette première étape.

Pour cette première étape, une modification exige une connexion au serveur.
Le PC peut consulter une copie locale hors connexion. Aucune file de mutations
hors ligne ni fusion automatique de changements concurrents n'est introduite.

## Situation du dépôt et de l'hébergement

- `MS3DPRINT.Manager.Core` contient les règles métier et des services de stockage
  dans l'arborescence de l'atelier. Les fiches clients et projets sont des JSON.
- `ClientProfileStore` et `ProjectProfileStore` proposent des mises à jour avec
  contrôle de `UpdatedAt`. `ProjectTasks` utilise déjà cette protection.
- `ManagedFileService`, `EntityManagementService` et `WorkspacePathSafety`
  assurent des opérations sur les fichiers, la corbeille et des contrôles de
  chemins. Leur comportement doit être réutilisé et testé sous Linux.
- L'interface PC et Android repose sur Avalonia. Android échange actuellement
  `etat-pc.json`, achats, recettes et pièces jointes dans un dossier Nextcloud.
  Ce mécanisme ne constitue pas une API d'accès complet à l'atelier.
- Les anciens projets Coolify MS3DFORGE et MS3DFORGE-V2 ont été supprimés.
  MS3DPRINT-FR et MS3DPRINT-SHOP sont exclus de toute modification.
- La suppression des projets ne prouve pas le nettoyage du disque hôte :
  `/var/ms3dprint/uploads`, les anciennes images et les sauvegardes éventuelles
  restent à inventorier séparément. Le nouveau service utilise un stockage neuf.

## Architecture retenue

Un service ASP.NET Core .NET 10 fournit une API authentifiée et une interface
web adaptée au téléphone et au PC. Il réutilise Core pour les validations,
les calculs et les opérations métier. Le web est développé dans ce dépôt,
avec son serveur, pour conserver une seule autorité métier.

Manager PC et le compagnon Android appellent la même API. Un client réseau
commun gère les contrats, les erreurs et les versions ; chaque interface garde
les éléments adaptés à son support. Le rendu Avalonia n'est pas supposé
portable tel quel dans un navigateur.

Une seule instance du serveur possède l'atelier. Les mutations passent toutes
par ses services ; aucun partage SMB, montage Nextcloud ou dossier synchronisé
ne permet à un second processus d'écrire dans cet atelier. Les formats métier
existants sont conservés pour limiter la migration. Un volume persistant porte
l'atelier et un espace distinct porte les secrets et métadonnées du service.
Les téléchargements temporaires restent séparés des fichiers validés.

Les identités, sessions et reçus de commandes utilisent SQLite et les
composants d'authentification ASP.NET Core. Les fichiers métier ne sont pas
dupliqués dans une nouvelle base relationnelle pour cette première étape.
Le stockage est sauvegardé de manière cohérente avec les métadonnées du service.

## Connexion et accès

- Un compte propriétaire privé, sans inscription publique ni accès anonyme à
  l'atelier. Les écrans de connexion n'affichent aucune information métier.
- Mot de passe haché avec les composants standard du framework ; aucune
  conservation du mot de passe en clair et aucune présence dans Git ou les logs.
- Sessions web par cookie sécurisé, HttpOnly, protection CSRF des mutations et
  limitation des tentatives de connexion. Les clés de protection persistent
  entre redéploiements. La déconnexion invalide la session côté serveur.
- Les clients PC/Android utilisent des sessions révocables propres à l'appareil,
  avec jetons stockés dans les protections du système et hachés côté serveur.
  Les jetons ne sont jamais inclus dans une URL ou un journal applicatif.
- HTTPS obligatoire en production. Aucun fichier d'atelier servi par une URL
  publique statique ; téléchargement et aperçu vérifient la session.
- Le premier compte et les secrets de production sont configurés au déploiement,
  sans mot de passe prévisible, compte de démonstration ou accès public temporaire.

## Données, synchronisation et concurrence

Les contrats HTTP manipulent des identifiants, des références et des chemins
relatifs validés. Un client ne peut jamais choisir une racine disque serveur.
La version renvoyée avec une fiche doit accompagner sa modification. Une
version périmée produit un conflit explicite : l'écran conserve le brouillon,
propose de recharger l'état serveur et ne remplace pas silencieusement celui-ci.

Pour les fichiers, la version est issue de leur contenu et de leur identité
dans l'inventaire. Un remplacement exige la version connue du fichier et une
confirmation. Un nom en conflit ne provoque aucun écrasement automatique.
Les liens symboliques, traversées de chemins, racines absolues, collisions de
casse Windows/Linux et noms incompatibles Windows sont refusés à l'entrée.

Les mutations sont sérialisées côté serveur pour protéger les opérations qui
touchent plusieurs fichiers. Les journaux et reprises existants sont conservés.
Une commande non triviale porte un identifiant de requête. Son résultat durable
permet de consulter son état après une coupure et de prévenir les doublons.
Si une opération interrompue ne peut pas être déterminée avec certitude, elle
est signalée comme telle ; elle n'est pas rejouée automatiquement à l'aveugle.

Le PC synchronise uniquement une copie de consultation, dans un dossier de
cache distinct et identifié comme tel. Les actions du mode distant envoient
une commande au serveur ; elles n'écrivent pas d'abord dans le cache.
Les fichiers locaux externes ne deviennent pas des modifications serveur
implicites. La perte de connexion désactive les mutations et indique la date
de dernière synchronisation. Elle ne bascule jamais en écriture locale.

Un changement de version de l'API incompatible bloque les mutations et invite
à mettre à jour l'application. Les versions et erreurs sont traitées de la
même manière sur PC et Android.

## Parcours de la première étape

### Clients et projets

Connexion, recherche et consultation des clients et projets, création,
modification des fiches, échéances, priorité, statut, archivage et restauration.
Les références et codes sont attribués par le serveur avec les règles existantes.
Les renommages et déplacements passent par les services métier, avec contrôle
de version et vérification des liens entre client, projet et documents.

### Tâches

Ajout, modification du texte et des notes, validation, réouverture,
réorganisation et retrait. L'ordre et les règles de prochaine action sont
ceux de `ProjectTasks`. Une tâche terminée ne clôture pas automatiquement
un projet. Un retrait demande confirmation.

### Fichiers

Navigation dans les dossiers autorisés, ajout de dossiers, téléversement,
téléchargement, renommage, déplacement, corbeille et restauration. Les fichiers
sont transférés en flux avec des limites configurées et une reprise explicite
après échec. Un transfert partiel ne devient jamais un document visible comme
valide. L'inventaire affiche taille et état ; le serveur vérifie l'empreinte du
contenu avant de valider le transfert. Les exécutables ne sont pas lancés.

Une suppression, un retrait ou un remplacement affiche l'élément et la
conséquence, avec annulation privilégiée, conformément aux consignes du dépôt.
Les aperçus sécurisés de formats simples peuvent suivre ; le visualiseur 3D
complet ne conditionne pas la validation de cette première étape.

### PC et Android

Le PC distingue clairement le mode atelier local existant et le mode serveur.
Le passage au mode serveur est explicite, après vérification de la migration.
L'atelier local d'origine est conservé et identifié comme copie antérieure à
la bascule ; il n'est pas présenté comme un deuxième atelier actif synchronisé.
Le retour au mode local demande une procédure d'export cohérente, pas une simple
réactivation de l'ancienne copie devenue périmée.

Android gagne un accès serveur aux parcours de cette étape. L'ancien échange
Nextcloud reste disponible pour les ateliers locaux mais n'alimente pas
simultanément le même atelier distant. Les fonctions achats/recettes déjà
présentes sur Android ne sont pas annoncées connectées au serveur avant leur
étape dédiée.

## Migration sans perte

1. Travailler d'abord sur un atelier fictif et une destination serveur neuve.
2. Inventorier les fiches, références, documents et fichiers cachés utiles,
   y compris `.ms3dprint-manager`. Signaler les JSON illisibles, les liens, les
   chemins non portables, les doublons et les fichiers indisponibles.
3. Pour la migration réelle, créer et vérifier une sauvegarde de l'atelier
   source. Mettre les écritures en pause et détecter toute modification de la
   source pendant l'export ; une source qui change invalide la migration.
4. Transférer vers un espace de préparation non accessible comme atelier actif.
   Comparer les chemins relatifs, nombres, tailles et empreintes SHA256,
   puis vérifier la lecture des fiches et les relations métier.
5. Ne pas modifier les identifiants, les références ni le contenu des documents.
   Les chemins absolus contenus dans des métadonnées doivent être recensés et
   adaptés par une conversion explicite et testée, sans remplacement global
   de chaînes. Un module non encore porté reste préservé et indisponible à
   l'édition distante ; aucune donnée ne doit être abandonnée pour l'import.
6. Produire un bilan consultable. Aucune bascule si un contrôle échoue.
7. Activer l'atelier serveur après validation de l'utilisateur ; conserver
   la source et la sauvegarde. Une migration interrompue peut être reprise
   ou abandonnée sans altérer l'atelier actif.

## Hébergement et exploitation

Créer une ressource Coolify dédiée à ce nouveau service, avec une image Docker
reproductible, une vérification de santé ne révélant pas de données et un volume
persistant neuf. Le compte de service dispose seulement des droits nécessaires
sur ses dossiers. La base SQLite n'est pas exposée par un port public.

Brancher le domaine et HTTPS après validation du service sur des données fictives.
Une sauvegarde doit être disponible hors du volume de production, et une
restauration doit être testée avant la bascule réelle. La destination choisie
et les secrets correspondants sont configurés avec le propriétaire ; aucune
sauvegarde n'est réputée assurée par la seule persistance Docker.

Les déploiements et migrations de format sauvegardent l'état précédent et
refusent un retour à un binaire incompatible. Un redémarrage ne perd ni comptes,
ni sessions attendues, ni documents. Les journaux masquent les secrets et ne
reproduisent pas les documents ou informations clients.

Les deux ressources commerciales et le proxy partagé ne sont ni supprimés,
ni redémarrés, ni reconfigurés pour le nettoyage ou les tests de ce service.

## Vérifications exigées

- Tests métier existants et nouveaux cas de conflit sur clients, projets,
  tâches et fichiers ; vérifications de compatibilité Windows/Linux.
- Tests API : accès sans session refusé, sessions révoquées, CSRF web,
  limites des requêtes, traversée de chemins, liens, noms incompatibles et
  isolation des fichiers d'authentification.
- Deux appareils modifiant le même projet : le second conserve son brouillon
  et reçoit un conflit. Deux créations concurrentes ne partagent pas une référence.
- Coupure réseau et redémarrage pendant création, déplacement et transfert :
  pas de doublon, document tronqué, perte silencieuse ou rejeu non vérifié.
- Migration interrompue, fichier source modifié et empreinte incorrecte :
  refus de bascule, source inchangée et bilan exploitable.
- Parcours complet web, PC et Android sur données fictives : créer un client,
  créer son projet, modifier ses tâches, ajouter/télécharger un document,
  renommer, mettre à la corbeille et restaurer ; état cohérent après reconnexion.
- Essai PC éteint : web et Android continuent de lire et d'écrire. Essai réseau
  coupé sur PC : copie consultable, écritures bloquées et état explicite.
- Redéploiement Docker et restauration d'une sauvegarde testés. Contrôle
  en lecture seule de l'état des ressources MS3DPRINT-FR et MS3DPRINT-SHOP.

## Suite de l'objectif complet

Après cette première étape, porter collections, matières, imprimantes,
estimateur/devis, achats, recettes, factures, échéanciers, exports, reconnaissance
de documents et visualisation. Chaque module doit utiliser la même autorité
serveur et disposer de critères de parité explicites sur les trois supports.

Les intégrations qui ouvrent une application Windows, un slicer ou un fichier
dans un logiciel tiers sont distinguées des actions métier : elles nécessitent
un appareil équipé et connecté ou une adaptation spécifique. Le navigateur ne
peut pas exécuter arbitrairement un programme du PC éteint. Cette limite doit
être affichée au niveau des fonctions concernées, sans promettre une parité
technique impossible.

Le renommage commercial global vers MS3DForge est une livraison distincte.
L'exécutable Windows reste nommé exactement `MS3DPRINT Manager.exe` tant que
la consigne de livraison n'est pas modifiée explicitement.

## État de ce document

Ce document formalise l'architecture et le premier périmètre approuvés dans
le chat. Sa relecture précède le plan d'implémentation détaillé. Il ne décrit
pas des fonctions déjà développées, testées ou déployées.
