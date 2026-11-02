# Socle du serveur distant

Cette tranche prépare le futur accès web, PC et Android. Elle fournit une API
privée, un compte propriétaire et la consultation des fiches clients/projets.
Elle permet aussi d'exporter et de préparer une migration isolée avec bilan.
Elle ne fournit encore ni interface web, ni activation d'un atelier migré,
ni modification métier, ni connexion des applications PC/Android. Elle n'est pas un déploiement de
l'accès distant complet sur `ms3dforge.fr`.

## Stockage et connexion

Le serveur utilise deux dossiers absolus distincts, non imbriqués :

- `Server__StateRoot` : compte haché, sessions hachées et clés de protection CSRF.
- `Server__WorkspaceRoot` : atelier de test préalablement créé ; lecture des
  fiches JSON existantes via Core, sans synchronisation Nextcloud du dossier.

Le dossier d'état et ses sauvegardes doivent rester privés : les clés CSRF
persistantes ne sont pas chiffrées au repos par l'application. Protéger le
volume et les sauvegardes au niveau de l'hébergement.

Le serveur refuse une configuration absente, relative, imbriquée ou contenant
un lien symbolique. Il ne crée pas de compte automatiquement. N'utiliser
aucune copie de l'atelier réel avant la livraison et la validation du migrateur.

Les fiches sans métadonnées ne sont pas transformées en comptes ou projets
automatiquement. Une fiche JSON illisible fait échouer sa liste avec HTTP 500,
sans exposer son chemin ni présenter un inventaire partiel comme complet.

## Construction

Depuis la racine du dépôt :

```powershell
dotnet test tests/MS3DPRINT.Manager.Server.Tests '-bl:outputs/server-tests-{}.binlog'
docker build -f src/MS3DPRINT.Manager.Server/Dockerfile -t ms3dforge-server:dev .
docker build --target verify -f src/MS3DPRINT.Manager.Server/Dockerfile .
```

Exemple d'instance locale sur un volume neuf :

```powershell
docker volume create ms3dforge-server-test
docker run -d --name ms3dforge-server-test -p 127.0.0.1:5080:8080 --mount source=ms3dforge-server-test,target=/data ms3dforge-server:dev
```

`http://127.0.0.1:5080/health` retourne uniquement `{"status":"ok"}`. Toutes
les autres routes exigent HTTPS ; cet exemple ne permet donc que le contrôle
de démarrage. Un faux `X-Forwarded-Proto: https` n'active pas l'accès privé.

Pour tester les échanges HTTPS, utiliser Kestrel avec un certificat de
développement approuvé et des dossiers fictifs explicitement configurés.
Pour Coolify, configurer d'abord la confiance envers l'adresse réelle de son
proxy dans une tranche dédiée : le serveur ne fait actuellement confiance à
aucun en-tête de proxy. Ne pas publier le port interne sur Internet ni désactiver
la vérification HTTPS pour contourner ce prérequis.

## Création explicite du propriétaire

Exécuter une fois le binaire avec `--create-owner`, dans le même environnement
de stockage que le service. La commande utilise `MS3DFORGE_OWNER_NAME` et
`MS3DFORGE_OWNER_PASSWORD`, puis s'arrête sans ouvrir de port. Le mot de passe
doit contenir de 16 à 1024 caractères. Un compte existant n'est jamais remplacé.

Dans Docker, utiliser la commande sur le conteneur prévu :

```powershell
docker exec --env MS3DFORGE_OWNER_NAME --env MS3DFORGE_OWNER_PASSWORD ms3dforge-server-test dotnet MS3DPRINT.Manager.Server.dll --create-owner
```

Les valeurs viennent de l'environnement du terminal de l'administrateur ;
ne pas mettre le mot de passe dans la ligne de commande, le dépôt, une capture
ou une configuration persistante de Coolify. Retirer les deux variables après
usage. Aucun endpoint d'inscription ou de réinitialisation publique n'existe.
La rotation du mot de passe et la gestion des appareils seront ajoutées avant
le déploiement de production.

## Contrats HTTP v1

Toutes les réponses portent `Cache-Control: no-store`. Aucun CORS n'est autorisé.

| Route | Comportement |
| --- | --- |
| `GET /health` | Santé minimale, sans connexion ni données d'atelier |
| `GET /api/v1/auth/csrf` | Dépose un cookie CSRF et renvoie `requestToken` |
| `POST /api/v1/auth/login` | JSON `name`, `password` ; 401 si incorrect |
| `GET /api/v1/auth/session` | 200 si connecté, sinon 401 |
| `POST /api/v1/auth/logout` | Révoque la session courante côté serveur ; 204 |
| `GET /api/v1/clients` | Résumés des fiches clients, identifiants et versions |
| `GET /api/v1/projects` | Résumés des fiches projets, versions et tâches ordonnées |

Navigateur : appeler `/csrf`, puis envoyer `X-CSRF-TOKEN` avec le jeton reçu
et les cookies pour `/login`. Le cookie de session est Secure, HttpOnly,
SameSite=Strict et préfixé `__Host-`. Après connexion, redemander un jeton CSRF
lié à la session avant `/logout`. Aucun jeton de session n'est retourné dans
le corps de la réponse web.

Client natif : envoyer `X-MS3DFORGE-Client: native` à `/login`, sans Origin
ni cookie de session. La réponse fournit `accessToken` et `expiresIn` ; utiliser
ensuite `Authorization: Bearer ...`. Ne jamais placer le jeton dans une URL.
Le serveur refuse de combiner Bearer et cookie de session dans une même requête.

Les sessions expirent après 8 heures, sans renouvellement automatique à ce
stade. Une déconnexion invalide immédiatement une copie du cookie ou du jeton.
Les tentatives de connexion sont limitées à 20 par minute pour l'instance ;
HTTP 429 demande d'attendre. Le serveur est conçu pour un propriétaire et
une seule instance ; aucun équilibrage entre plusieurs instances.

## Vérifications et étapes suivantes

### Préparer une migration sur des données de test

Ces commandes sont administratives et locales ; elles n'ouvrent pas de port
HTTP, ne transfèrent rien vers Internet et ne changent pas la racine active.
Utiliser un atelier fictif pour cette tranche. Avant un export réel, il faudra
fermer Manager et suspendre les autres écritures, y compris celles arrivant
par synchronisation, puis valider le parcours de sauvegarde et de bascule.

Après construction du projet serveur et configuration de ses deux racines :

```powershell
dotnet build src/MS3DPRINT.Manager.Server '-bl:outputs/server-migration-build-{}.binlog'
dotnet src/MS3DPRINT.Manager.Server/bin/Debug/net10.0/MS3DPRINT.Manager.Server.dll --export-migration C:/Temp/atelier-test-export
```

Le dossier d'export doit être neuf, avec un parent existant, en dehors de la
source. L'export inventorie les fichiers, valide les métadonnées et relations
clients/projets, puis produit `workspace.zip` et `report.json` si les contrôles
sont favorables. L'inventaire source est comparé après export, y compris ses
SHA256. Le serveur ne verrouille pas les écritures d'applications tierces :
l'arrêt de ces écritures reste nécessaire.

Pour préparer la restauration d'une archive transférée par l'administrateur,
reprendre l'empreinte affichée à l'export, depuis un canal de confiance :

```powershell
dotnet src/MS3DPRINT.Manager.Server/bin/Debug/net10.0/MS3DPRINT.Manager.Server.dll --stage-migration C:/Temp/atelier-test-export/workspace.zip --sha256 EMPREINTE_SHA256_DE_L_EXPORT
```

Cette commande exige une archive complète avec documents. Elle en fait une
copie privée, vérifie l'empreinte attendue, puis restaure sous un nouveau dossier
`Server__StateRoot/migrations/<identifiant>/workspace`. Les octets extraits sont
comparés au manifeste original et les métadonnées relues. Le bilan est écrit
dans le dossier de préparation ; le serveur continue d'utiliser son ancien
`Server__WorkspaceRoot`.

- Code de sortie **0** : les contrôles de ce lot sont favorables ; une revue
  reste nécessaire. Ce résultat n'active rien.
- Code **2** : contrôle refusé. Consulter `report.json` s'il a pu être créé.
  Un dossier déjà existant est refusé avant toute écriture.
- Code **1** : arguments de commande invalides.

Une exclusion inconnue, un JSON/JSONL illisible, une référence client absente,
une référence projet en double, un dossier sans fiche ou une incohérence de
chemin bloque le bilan. Les chemins absolus et les séparateurs Windows dans
les champs de chemin exigent une conversion explicite ; ce lot ne réécrit pas
les données. Les métadonnées et journaux de plus de 16 Mio sont conservés sur
la source et signalés comme nécessitant un contrôle en flux avant migration.

Le périmètre copié est celui de la sauvegarde complète : dossiers métier,
`.ms3dprint-manager` (historiques et modules non portés inclus) et registres de
codes clients reconnus dans `data`. Les autres entrées sont listées comme
exclusions bloquantes. L'indicateur `.ms3dprint-manager/last-backup.json` est
explicitement exclu avec un avertissement, car il ne décrit pas la nouvelle
sauvegarde. Les liens, collisions de casse et noms non portables sont refusés.

Une interruption ou un audit défavorable peut laisser une archive et une copie
partielle ou préparée à examiner. Rien n'est supprimé automatiquement et une
nouvelle tentative utilise un nouveau dossier. Il n'existe pas encore de reprise
réseau, de conversion de tous les anciens formats ni de commande d'activation.
Un bilan favorable ne valide pas les règles comptables des modules non encore
portés : ceux-ci restent indisponibles à la modification distante.

### Couverture

Les tests utilisent uniquement des dossiers temporaires et des secrets générés
pour chaque exécution. Ils couvrent expiration, hachage, révocation, CSRF,
HTTPS, refus des origines web sur le login natif, limitation des tentatives,
configuration des dossiers et lecture des métadonnées sans chemins absolus.

Avant production : conversion des anciens chemins et activation validée, commandes versionnées et
reçus durables, transferts et corbeille, clients web/PC/Android, confiance proxy
explicite, configuration du domaine, limites de stockage, sauvegarde extérieure
et restauration testée. Les projets Coolify MS3DPRINT-FR et MS3DPRINT-SHOP
restent exclus de ce chantier.
