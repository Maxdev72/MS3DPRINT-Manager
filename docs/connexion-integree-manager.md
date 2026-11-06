# Connexion à l’atelier serveur dans Manager

La connexion se trouve dans la fenêtre principale de MS3DPRINT Manager.
Sur un PC sans atelier existant, l’application propose la connexion serveur
au démarrage et conserve les pages habituelles **Accueil**, **Clients** et
**Projets**. Si un atelier PC contient déjà des données et qu’aucun mode n’a été
choisi auparavant, Manager conserve cet atelier local au premier lancement.
Les données PC de cet utilisateur sont plus récentes que la copie serveur :
leur reprise doit être vérifiée avant de basculer l’activité vers le serveur.

1. Vérifier l’adresse `https://ms3dforge.fr`, puis saisir le compte et son mot
   de passe directement dans Manager. Aucun navigateur n’est nécessaire.
2. **Mémoriser la connexion sur cet appareil** conserve seulement la session
   dans le gestionnaire d’identifiants Windows. Le mot de passe n’est jamais
   sauvegardé. La session expire après huit heures au maximum ; une révocation
   exige également de se reconnecter.
3. Le bandeau indique le compte, le serveur et la dernière lecture. Les clients,
   projets et tâches sont actualisés toutes les 30 secondes. Les modifications
   passent immédiatement par le serveur, avec contrôle des versions.
4. Hors connexion, la dernière copie reste consultable. Les écritures sont
   bloquées. Le retour du réseau permet de reprendre les lectures si la session
   est toujours valide ; une coupure ne bascule jamais en écriture locale.

**Atelier local** permet un changement explicite vers l’atelier historique du
PC. Le choix du mode est mémorisé. Cet atelier reste indépendant des données du
serveur : le bouton n’effectue ni fusion, ni import de fichiers. Pour revenir
au serveur, utiliser **Atelier serveur**. Une opération ou un formulaire en cours
doit être terminé avant de changer de mode.

Les modules et actions pas encore exposés par l’API restent indisponibles en
mode serveur : fichiers, classement, devis, factures, achats/recettes, stocks,
imprimantes, collections et visualiseur. Le mode local conserve leurs fonctions.
Les champs non modifiables par l’API sont indiqués comme tels dans les formulaires.
Les références et identifiants des données ne sont pas remplacés par des chemins
Windows ; aucune racine de l’atelier local n’est utilisée en mode serveur.

Une réponse perdue ne provoque aucun renvoi automatique. Manager conserve
l’identifiant de la commande avant l’envoi ; vérifier son reçu avant une
nouvelle opération. Une commande dont le résultat reste incertain garde les
écritures bloquées, même après redémarrage. Un changement concurrent conserve
la saisie et demande une reprise explicite, sans écrasement silencieux.

La copie de consultation reste dans
`%LOCALAPPDATA%/MS3DPRINT Manager/remote/copy.json` ; elle contient des données
métier locales, sans mot de passe ni jeton. La session mémorisée réside dans
le coffre Windows. Une seule instance possède la copie et le journal à la fois.
La déconnexion oublie la session sur cet appareil, même si le serveur ne répond
pas ; une commande en attente reste conservée pour vérification.

Cette intégration concerne l’exécutable PC `MS3DPRINT Manager.exe`. Elle réutilise
les API déjà déployées pour la connexion habituelle. Le nouvel assistant de reprise
des données PC exige une mise à jour séparée du serveur : voir
[le guide de reprise](reprise-atelier-pc-serveur.md).
La création autonome de comptes clients avec ateliers isolés reste un chantier
séparé. L’application Android précédente n’est pas remplacée par ce lot.

Sous Ubuntu et Linux Mint, la connexion mémorisée utilise le trousseau Secret Service du bureau ; voir [la notice Linux](linux.md).
