# Connexion intégrée à Manager — conception validée

Le 3 novembre 2026, l’utilisateur approuve dans le chat : connexion au lancement,
Accueil/Clients/Projets dans la fenêtre principale sur les données serveur,
session mémorisée de façon sécurisée, copie de consultation hors connexion,
changement explicite vers l’atelier local et modules non connectés signalés.
Le développement se fait sans prise de contrôle du PC ni de Coolify.

## Parcours retenu

La fenêtre habituelle de Manager conserve sa navigation, ses styles et ses
pages Accueil, Clients et Projets. Au premier démarrage, le compte se connecte
à https://ms3dforge.fr directement dans cette fenêtre. Un bandeau permanent
indique le serveur, le compte, l’état connecté/hors connexion et la date de
synchronisation. L’écran de connexion reste accessible depuis ce bandeau.
Il ne faut ni ouvrir un navigateur ni une seconde fenêtre d’atelier.

La session native, valable huit heures côté serveur, peut être mémorisée dans
le gestionnaire d’identifiants Windows. Le mot de passe n’est jamais enregistré.
Au lancement suivant, restaurer seulement une session non expirée liée au même
compte et serveur ; vérifier son accès en lecture. Une révocation ou expiration
réaffiche la connexion, sans basculer en local ni réessayer un mot de passe.
La déconnexion oublie la session persistante même en cas de panne du serveur.

La lecture clients/projets/tâches est automatique toutes les 30 secondes. La
copie séparée existante est consultable hors connexion ; toutes les écritures
exigent la connexion, la bonne version et le journal durable des commandes.
Une réponse perdue ou une fermeture en cours d’envoi reste bloquante jusqu’au
reçu. Aucun rejeu automatique. Un brouillon ne doit pas changer de version lors
d’une lecture automatique. Une erreur n’efface ni copie saine ni brouillon.

## Source des pages et limites explicites

En mode serveur, les pages utilisent les contrats API et leurs identifiants,
jamais des chemins de l’atelier Windows, même pour ouvrir une fiche ou une tâche.
Les listes conservent recherche et tri usuels. Créer/modifier les clients et
projets, puis gérer les tâches utilise les API déjà déployées. Les champs non
exposés par ces API ne deviennent pas silencieusement éditables.

Les modules fichiers, devis, factures, stocks et autres opérations non exposées
sont explicitement indisponibles en mode serveur. Ils ne doivent pas lire ou
modifier le dossier local en arrière-plan. Un changement explicite « Atelier
local » donne accès à l’ancien atelier indépendant ; ne jamais le présenter
comme une copie synchronisée. Mémoriser ce choix de mode. Le compte serveur
reste privé, sans inscription publique ni gestion multi-atelier dans ce lot.

Les commandes et rafraîchissements doivent garder leur connexion d’origine.
Un changement de mode ou fermeture annule les lectures et empêche toute fin
asynchrone ancienne de remplacer l’affichage ou le journal d’une autre session.
Une seule fenêtre possède la copie et son journal via le verrou existant.

## Implémentation et validation

Un contrôleur Core gère connexion, copie, droits d’écriture et journal ; la vue
principale et le coffre Windows consomment des interfaces explicites. Les secrets
ne passent jamais par les préférences JSON ni la copie. Le coffre Windows utilise
les API du système, sans nouvelle dépendance. Les écrans principaux réutilisent
les composants et styles de l’application, avec des modèles de données distants.
Le mode local existant conserve ses parcours et tests. Les variables explicites
de choix d’atelier utilisées par les tests/démonstrations restent des demandes
locales et ne contactent pas un serveur implicitement.

Tests de connexion restaurée/révoquée, stockage secret, journal avant envoi,
fermeture/reprise, panne, conflit, séparation serveur/local et navigation réelle
Headless. Publication Windows en un seul « MS3DPRINT Manager.exe », sauvegarde
précédente sous outputs/backups. Aucun nouveau compte ou déploiement public.

## Priorité de reprise — précision utilisateur du 4 novembre

L’utilisateur confirme que les données PC (clients, projets, tâches et fichiers)
sont plus récentes que la copie serveur. Au premier lancement, en l’absence
d’un choix de mode explicite enregistré, un atelier local contenant des données
doit donc rester ouvert en mode local. Le serveur ne doit pas être présenté
comme une migration récente de ces données. Un PC sans atelier existant garde
la connexion serveur au démarrage. La détection locale est bornée, en lecture
seule et faite avant l’activation du mode, sans explorer le dépôt de développement.

La connexion intégrée reste disponible par un choix explicite. Un avertissement
précise que l’ouverture du serveur ne transfère pas les données du PC et que
sa copie peut être antérieure. Aucun remplacement ou import aveugle n’est permis.
Une migration ultérieure doit sauvegarder, comparer et vérifier la reprise des
évolutions PC avant de faire du serveur la référence opérationnelle.
