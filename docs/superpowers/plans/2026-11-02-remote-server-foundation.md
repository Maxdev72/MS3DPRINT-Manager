# Socle serveur distant — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task in this session. Steps use checkbox syntax for tracking.

**Goal:** Fournir une API privée exécutable et testée, indépendante du PC, avec
compte propriétaire, sessions révocables et consultation de clients/projets de test.

**Architecture:** Une application ASP.NET Core .NET 10 réutilise Core pour lire
un atelier choisi par l'administrateur. SQLite conserve le propriétaire et les
empreintes des sessions ; aucun secret n'est placé dans l'atelier. Cette tranche
ne modifie ni l'interface actuelle, ni les données réelles, ni Coolify.

**Tech Stack:** .NET 10, ASP.NET Core, Microsoft.Data.Sqlite, xUnit,
Microsoft.AspNetCore.Mvc.Testing, Docker.

**Spec:** `docs/superpowers/specs/2026-11-02-acces-distant-design.md`.

## Global Constraints

- Serveur référence ; aucune écriture locale implicite pendant une coupure.
- Première tranche sur données fictives, sans transfert de l'atelier réel.
- MS3DPRINT-FR et MS3DPRINT-SHOP exclus de toute modification.
- Aucun compte public, inscription publique ou mot de passe prévisible.
- Exécutable livré : `MS3DPRINT Manager.exe` ; pas de livraison PC dans cette tranche.
- Une seule instance serveur ; dossiers d'état et d'atelier distincts.
- Cookie sécurisé, CSRF, limite de tentatives, jetons hachés, révocation persistante.

## Découpage de la conception

Ce plan couvre uniquement le socle serveur. Les tranches suivantes couvrent,
dans l'ordre : migration et inventaire vérifié ; commandes clients/projets/tâches
avec version et reçus durables ; transferts et corbeille ; client réseau commun ;
interfaces PC/Android/web ; validation Linux et déploiement privé ; bascule réelle.
Les modules comptables et métiers supplémentaires restent la suite du périmètre
global. La fin de ce plan ne signifie pas que l'accès distant complet est livré.

## Fichiers et responsabilités

- `src/MS3DPRINT.Manager.Server/Program.cs` : composition et démarrage.
- `ServerApplication.cs` : middleware, routes santé, authentification, consultation.
- `ServerStorage.cs` : configuration des deux dossiers et contrôles de séparation.
- `Authentication/SessionStore.cs` : SQLite, propriétaire haché, sessions et révocation.
- `Authentication/SessionAuthenticationHandler.cs` : vérification cookie/jeton par requête.
- `Authentication/AuthEndpoints.cs` : connexion, CSRF, session courante, déconnexion.
- `Workspace/WorkspaceReader.cs` : conversion Core vers contrats sans chemins absolus.
- `Contracts.cs` : requêtes et réponses HTTP explicites.
- `tests/MS3DPRINT.Manager.Server.Tests/` : tests portables d'intégration et de stockage.
- `src/MS3DPRINT.Manager.Server/Dockerfile` : image Linux non privilégiée.
- `docs/acces-distant-serveur.md` : exécution locale et limites de cette tranche.

### Task 1 : Service démarrable, fermé par défaut

**Interfaces:** `ServerApplication.Build(string[] args)` produit une
`WebApplication`. `ServerStorage` fournit `StateRoot` et `WorkspaceRoot`.

- [x] Ajouter projet serveur et projet de tests `net10.0`, références Core et
  `Microsoft.AspNetCore.Mvc.Testing`/xUnit ; ajouter les projets à la solution.
- [x] Écrire le test :

```csharp
[Fact]
public async Task Health_does_not_disclose_workspace_and_private_routes_require_login()
{
    using var host = new ServerFixture();
    using var client = host.CreateClient();
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized,
        (await client.GetAsync("/api/v1/clients")).StatusCode);
}
```

- [x] Exécuter `dotnet test tests/MS3DPRINT.Manager.Server.Tests -bl:outputs/server-red-{}.binlog`
  et constater l'absence des routes attendues.
- [x] Implémenter le démarrage, santé minimale, authentification obligatoire,
  erreurs ProblemDetails sans chemins ou détails internes ; refuser configuration
  absente, racines imbriquées, et liens dans les dossiers configurés.
- [x] Rejouer les tests et ajouter les cas de configuration invalide.

### Task 2 : Propriétaire privé et sessions persistantes

**Interfaces:** `SessionStore.CreateOwner(string name, string password)`
crée uniquement le premier propriétaire ; `VerifyOwner(string name, string password)`
vérifie le hachage ; `CreateSession()` renvoie le jeton éphémère brut ;
`IsActive(string token)` et `Revoke(string token)` ne stockent que son empreinte.

- [x] Écrire les tests : mauvais mot de passe refusé ; pas d'endpoint register ;
  cookie Secure/HttpOnly/SameSite ; CSRF obligatoire pour login/logout web ;
  session native par Bearer ; révocation immédiatement effective sur un cookie
  copié ; session conservée après réouverture du stockage ; secret absent de SQLite.
- [x] Constater les échecs, puis créer la base SQLite avec requêtes paramétrées,
  `PasswordHasher`, jetons aléatoires de 256 bits hachés SHA256, expiration 8 heures.
- [x] Ajouter un bootstrap CLI explicite `--create-owner` lisant nom et mot de
  passe dans l'environnement pour cette seule commande, sans journaliser le secret.
  Le démarrage normal ne crée aucun propriétaire automatiquement.
- [x] Mapper `/api/v1/auth/csrf`, `/login`, `/session`, `/logout` ; login JSON natif
  sélectionné par `X-MS3DFORGE-Client: native`, sans Origin ni cookies. Ne pas
  autoriser CORS. Pour le web, valider l'antiforgery avant toute vérification
  de mot de passe ou révocation. Refuser une requête HTTP hors santé.
- [x] Limiter les tentatives de login avec RateLimiter, retourner 429 ; les
  réponses d'authentification sont `Cache-Control: no-store` et ne révèlent
  pas l'existence d'un nom. Clés DataProtection persistantes côté état.
- [x] Rejouer les tests positifs et négatifs, dont expiration avec horloge contrôlée.

### Task 3 : Consultation de l'atelier et exécution reproductible

**Interfaces:** `WorkspaceReader.ReadClients()` et `ReadProjects()` renvoient
des listes de contrats de consultation avec identifiants et versions, sans
chemins absolus, ni accès aux secrets du serveur.

- [x] Créer un atelier fictif dans les tests avec les stores Core, puis vérifier
  qu'une session authentifiée voit ses clients et projets, et que le JSON
  n'expose pas la racine de l'atelier. Une fiche corrompue produit une erreur
  explicite plutôt qu'une liste vide présentée comme complète.
- [x] Constater les échecs ; utiliser `ClientProfileStore.LoadAll()` et
  `ProjectProfileStore.LoadAll()` derrière une couche de lecture et projeter les
  champs nécessaires, sans endpoint de chemin arbitraire ni mutations dans ce plan.
- [x] Ajouter un test d'injection de racine par querystring : seule la racine
  configurée est lue ; vérifier qu'aucun secret d'état n'est téléchargeable.
- [x] Ajouter Dockerfile multistage SDK/runtime 10.0, utilisateur `app`, port 8080,
  chemins explicites `/data/state` et `/data/workspace`. Le proxy de production
  exige une configuration de confiance explicite ; aucune confiance universelle
  dans `X-Forwarded-*` n'est ajoutée.
- [x] Documenter la création du compte, les variables, tests, endpoints et
  limites : pas d'interface web, de migration, de mutations ni de clients connectés.
- [x] Exécuter les tests serveur et la solution, `git diff --check`, puis vérifier
  le diff. Docker Linux doit être construit et testé lorsque Docker est disponible ;
  sinon signaler précisément ce contrôle manquant.
- [x] Committer uniquement les fichiers de cette tranche après vérification.

## Vérification du lot — 2 novembre 2026

- Suite Windows : 1 296 tests réussis (1 209 Core, 73 estimateur, 14 serveur).
- Cible Docker verify : 14 tests serveur réussis sous Linux.
- Image runtime construite ; démarrage non-root (UID 1654), santé HTTP 200, accès privé HTTP 400 et propriétaire conservé après redémarrage vérifiés.
- Revue indépendante en lecture seule : aucun problème concret relevé pour le périmètre de ce lot.
- Aucun déploiement Coolify, transfert de données réelles ou changement du binaire livré.
