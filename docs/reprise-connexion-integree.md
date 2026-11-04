# Reprise du développement — connexion intégrée

Dépôt : dossier `sources/`, branche `feature/ms3dforge-remote`. Lire les deux
AGENTS.md (parent et dépôt) avant toute modification. Conception approuvée :
`docs/superpowers/specs/2026-11-03-integrated-server-workspace-design.md`.
Guide utilisateur : `docs/connexion-integree-manager.md`.

## Points d’entrée

- `MainWindow.Remote.cs` : mode, connexion au lancement, état permanent,
  rafraîchissement, générations et fermeture ; choix local explicite.
- `MainWindow.Remote.Pages.cs` : clients, projets et tâches dans les panneaux
  natifs de la fenêtre principale, avec identifiants API.
- `MainWindow.Remote.Forms.cs` : formulaires du panneau latéral, contrôle de
  version figé et reprise explicite après erreur/conflit ou réponse perdue.
- `RemoteWorkspaceConnection.cs` (Core) : session, copie, verrou, opérations
  sérialisées et journal durable avant tout envoi.
- `RemoteDeviceSession.cs` / `WindowsRemoteSessionStore.cs` : contrat de session
  et coffre Windows ; seul le jeton est conservé, jamais le mot de passe.
- `DesktopApplication.RemoteSessionStoreFactory` : liaison configurée par
  `MS3DPRINT.Manager.Windows.App/Program.cs`, sans dépendance Desktop → Windows.

La session native dure huit heures au maximum. Le redémarrage restaure une
session encore valide puis vérifie son accès. Le mode local historique conserve
ses fonctions et ne devient pas une copie synchronisée du serveur.

## Vérifications à préserver

`IntegratedServerWorkspaceUiTests` couvre démarrage, connexion restaurée ou
révoquée, erreur de coffre, navigation, coupure, commandes et reprise des reçus,
versions de brouillons, choix de mode persistant et refus pendant une écriture
de tâche locale. Tests contrôleur/coffre dans `RemoteWorkspaceConnectionTests`,
`RemoteDeviceSessionTests` et `WindowsRemoteSessionStoreTests`.

Les preuves et rapports locaux sont sous `outputs/integrated-server-workspace/`,
ignorés par Git. Ne pas y remplacer une vérification réelle par une affirmation.
Les captures Headless utilisent des données fictives. Les tests isolent
MS3DPRINT_WORKSPACE et MS3DPRINT_WINDOW_FILE dans des dossiers temporaires.
Le test de coffre utilise une cible unique et ne touche pas les identifiants réels.

Commandes PowerShell :

```powershell
dotnet test tests/MS3DPRINT.Manager.Core.Tests -c Release '-bl:outputs/core-{}.binlog'
dotnet test tests/MS3DPRINT.Manager.Server.Tests '-bl:outputs/server-{}.binlog'
dotnet publish src/MS3DPRINT.Manager.Windows.App -p:PublishProfile=PortableWinX64 -o outputs/MS3DPRINT-Manager '-bl:outputs/publish-{}.binlog'
```

Ne pas lancer deux builds du même dossier de sortie en parallèle : les DLL de
tests peuvent être verrouillées. Une livraison remplace l’unique exécutable
parent `MS3DPRINT Manager.exe`, après fermeture normale et comparaison SHA256.
Les anciennes versions vont uniquement dans `outputs/backups/`. Ne pas lancer
une nouvelle instance ou forcer la fermeture du logiciel.

## Hébergement et suite

L’application PC réutilise les API déjà déployées. Aucun changement de Coolify,
des données réelles ou des sites MS3DPRINT-FR / MS3DPRINT-SHOP n’est nécessaire
pour cette intégration. Le serveur de production reste épinglé au commit
`989d30f3ba11fb7b65787c569d1d894cfda1e8aa` ; ne pas annoncer que le nouveau code
a été déployé sur le serveur. Les archives métier et secrets restent hors Git.

Les modules fichiers, devis/factures, comptabilité, stocks, imprimantes,
collections et visualiseur ne sont pas portés par cette intégration : ils sont
indisponibles en mode serveur et utilisables dans le mode local indépendant.
La distribution à la clientèle avec comptes/ateliers isolés et mise en service
autonome reste un chantier distinct. L’APK précédent n’est pas remplacé ici.
La cible finale reste la parité métier entre les supports, avec le serveur
comme référence ; aucune fusion aveugle avec des écritures locales.

Précision utilisateur du 4 novembre : les données PC sont plus récentes que
la copie serveur. Le premier lancement sur un atelier existant sans choix de
mode enregistré doit donc rester local. L’ouverture explicite du serveur ne
réalise pas leur transfert. Il faut sauvegarder et comparer les évolutions PC
avant une nouvelle migration vérifiée ; aucune copie serveur ancienne ne doit
être présentée comme l’état le plus récent de l’atelier PC.

Validation finale du 4 novembre : 1 288 tests Core, 85 tests serveur,
45 tests ciblés contrôleur/coffre ; aucun échec. Revue source acceptée.
La suite Core contient les 11 cas ciblés de l’interface et de ses régressions.

Exécutable livré le 4 novembre : source `8e928a79f27cb72e93bcb362052e43140b651950`,
SHA256 `29CC14B2CA89B6DA430B958BCB9060E504F34B603B27CDD1A51121986998761B`.
Les nouvelles données PC ne sont pas encore reprises sur le serveur.
