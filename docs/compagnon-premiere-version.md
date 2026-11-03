# Première version du compagnon MS3DForge

Candidate du 2 novembre 2026, branche `feature/ms3dforge-remote`.

## Fonctions disponibles

Le serveur est la référence de son atelier. Il conserve les fiches et les tâches
sur son propre volume, même lorsque le PC est éteint. Le site web permet de créer
et modifier les clients, créer les projets, modifier leur état, échéance, priorité
et notes, puis ajouter, modifier, terminer, déplacer et retirer leurs tâches.
La référence des nouveaux projets est attribuée par les règles de Manager.

Sur PC : **Paramètres → Atelier → Accès serveur**. Sur Android : onglet
**Serveur**. Ces deux écrans permettent la connexion, la consultation des fiches,
la création de clients/projets et la gestion des tâches. La modification des
coordonnées clients et des champs projet est disponible sur le web dans cette
candidate. L'adresse à saisir est la racine HTTPS du serveur, par exemple
`https://ms3dforge.fr`, une fois le déploiement effectué.

Les modifications nécessitent une connexion. Une version périmée est refusée
sans écraser la version serveur. Après un envoi dont le résultat est inconnu,
utiliser **Vérifier le résultat** avant toute nouvelle saisie. Le serveur garde un
reçu durable pour éviter de réexécuter le même identifiant de commande.
Un reçu incertain nécessite un contrôle manuel ; il n'est jamais rejoué à l'aveugle.

## Limites de cette candidate

- L'atelier local existant et l'atelier serveur restent distincts. Il n'y a pas
  encore de synchronisation automatique du PC, ni de migration réelle effectuée.
- Documents/fichiers, estimateur, devis, factures, stocks et imprimantes restent
  dans Manager local. La parité complète est une étape ultérieure.
- La session, la dernière vue et les brouillons des interfaces distantes restent
  en mémoire. Fermer la vue ou recharger la page les perd. Résoudre un envoi
  incertain avant de fermer ; après fermeture, vérifier les données du serveur
  avant de saisir à nouveau une opération dont on ignore le résultat.
- L'APK est une candidate installable pour essai, sans validation sur téléphone
  physique. Les tests d'interface utilisent Avalonia Headless.
- Un seul serveur doit écrire dans un atelier. Ne jamais partager son volume avec
  un Manager local actif ou lancer deux réplicas serveur.

## Mise en place dans Coolify

Créer un projet séparé pour le compagnon. Ne modifier aucune ressource de
**MS3DPRINT-FR** ou **MS3DPRINT-SHOP**.

1. Source Git : ce dépôt, branche `feature/ms3dforge-remote`. Contexte de build :
   racine du dépôt `sources`. Dockerfile :
   `src/MS3DPRINT.Manager.Server/Dockerfile`. Cible finale par défaut, port `8080`.
2. Monter un volume persistant neuf sur `/data`, contenant les deux dossiers
   distincts `/data/state` et `/data/workspace`. Ils doivent être accessibles à
   l'utilisateur `app` (UID 1654). L'image initialise leurs droits ; le montage
   d'un volume Docker neuf a été vérifié avec cet utilisateur.
   Commencer par un atelier neuf. Aucun dossier commercial ou Nextcloud existant
   ne doit être monté.
3. Conserver `Server__StateRoot=/data/state` et
   `Server__WorkspaceRoot=/data/workspace`. Configurer
   `Server__TrustedProxyAddresses__0` avec **l'adresse IP exacte du proxy HTTPS
   vue depuis le conteneur**. D'autres IP exactes peuvent utiliser les indices 1, 2.
   Ne pas renseigner une plage réseau entière et ne pas activer
   `ASPNETCORE_FORWARDEDHEADERS_ENABLED`. Sans proxy explicitement connu, les
   requêtes HTTP autres que `/health` sont refusées, même avec un faux en-tête HTTPS.
4. Configurer `https://ms3dforge.fr` avec le certificat du proxy, sans publication
   directe du port 8080 sur Internet. Vérifier DNS et certificat. Contrôle de santé :
   `GET /health`, réponse 200 minimale. Une liste privée sans session renvoie 401.
5. Créer le propriétaire une seule fois dans le conteneur, en utilisant les
   variables éphémères `MS3DFORGE_OWNER_NAME` et `MS3DFORGE_OWNER_PASSWORD`, puis :
   `dotnet MS3DPRINT.Manager.Server.dll --create-owner`.
   Le mot de passe doit contenir au moins 16 caractères. Ne pas le placer dans Git,
   dans une commande journalisée ou dans les variables permanentes de Coolify.
   Retirer les variables de bootstrap aussitôt après la création. Il n'existe
   aucun compte par défaut et aucune inscription publique.
6. Avant usage réel, vérifier la connexion web puis PC/Android, une création
   fictive, sa consultation après redémarrage du service et la déconnexion.
   Sauvegarder l'ensemble du volume avant une mise à jour.

### Déploiement sur l'hébergement existant

Projet `MS3DFORGE-COMPAGNON`, application `ms3dforge-compagnon`, commit candidat
`989d30f3ba11fb7b65787c569d1d894cfda1e8aa`. Le volume propre à cette application
est `6hzhtpawlwcwvggxizmfxxhk-ms3dforge-compagnon-data`, monté sur `/data`.

Le serveur utilise déjà Nginx sur l'hôte avec un certificat pour `ms3dforge.fr`.
Son routage existant mène à `127.0.0.1:3002`. Le compagnon reprend ce port libre
avec le mapping **`127.0.0.1:3002:8080`** ; le port reste limité à l'hôte.
La passerelle Docker a été lue sur le serveur : **`10.0.1.1`**, seule adresse
autorisée par `Server__TrustedProxyAddresses__0`, à l'exécution uniquement.
Déploiement réussi le 3 novembre 2026 : `https://ms3dforge.fr` répond en HTTPS,
`/health` renvoie 200, les listes clients/projets sans session renvoient 401 et
`/identity.sqlite` renvoie 404. Le cookie CSRF est Secure, HttpOnly et SameSite=Strict.
Le contrôle de santé automatique Coolify reste désactivé ; ces vérifications
sont ponctuelles. Le compte propriétaire doit encore être créé directement par
l'utilisateur, puis sa connexion sur cet hébergement doit être validée.
Ne pas lancer de proxy Traefik/Caddy supplémentaire sur les ports 80/443 : le
routage et les certificats existants sont gérés par Nginx. Aucun fichier des deux
sites commerciaux n'a été modifié. Après déploiement, leurs conteneurs sont
toujours actifs : site principal depuis 23 heures, boutique depuis 4 semaines
et sa base MySQL depuis 5 semaines. Aucun de ces conteneurs n'a été redémarré.

La confiance au proxy suit les mécanismes ASP.NET Core documentés par
[Microsoft](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

## Vérifications de la candidate

1 227 tests Core et 73 tests Estimator réussis, puis 64 tests serveur réussis sous Windows et dans Docker Linux. Les 2 régressions
JavaScript vérifient la perte du corps de réponse et la conservation du brouillon.
Le parcours navigateur utilise un atelier et un compte fictifs : connexion,
création client/projet, ajout et validation d'une tâche, changement d'état et de
priorité, annulation du retrait par Entrée, brouillon conservé après actualisation.
L'affichage a été inspecté sur PC et à 390 px de large.

Le parcours local utilise un adaptateur de test limité à 127.0.0.1 devant le vrai
conteneur. Cet adaptateur n'est pas livré. Les cookies de production restent
Secure/HttpOnly et les clients natifs exigent HTTPS. Ce contrôle local ne vaut
pas validation du certificat public ni test réel sur Android.
