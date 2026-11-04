# Reprise guidée de l’atelier PC vers le serveur

Date : 4 novembre 2026. Objectif approuvé dans le chat : sauvegarder et comparer
les ateliers, montrer ajouts/modifications/conflits, transférer sans écrasement
silencieux puis travailler sur la référence serveur. Le PC contient actuellement
les données les plus récentes. Cette spécification détaille ce parcours approuvé.

## Périmètre et contraintes

- Développer et vérifier sans prise de contrôle du PC, navigateur ou Coolify.
- Aucun transfert réel, activation de données métier ou déploiement implicite.
- MS3DPRINT-FR et MS3DPRINT-SHOP exclus de toute modification.
- Même branche feature/ms3dforge-remote, dossier courant selon préférence utilisateur.
- Préserver les octets de l’atelier PC ; ne jamais y préparer ou restaurer la copie.
- Les sauvegardes réelles et secrets restent hors Git, hors image et hors répertoire web.
- Réutiliser WorkspaceBackupService, PortableWorkspaceMigrationService et les audits.
- Les dates métier, identifiants et références sont conservés. Les seules versions
  techniques UpdatedAt des fiches clients/projets importées et différentes sont
  renouvelées pour refuser toute ancienne commande ; tracer avant/après dans le rapport.
- Un conflit de contenu exige un choix explicite PC ou serveur, jamais une préférence
  déduite de la date. Les éléments uniquement serveur restent dans la copie résultante.
- Cette reprise conserve les fichiers et modules non portés. Elle ne les rend pas
  éditables en mode serveur. Les clients/projets/tâches utilisent ensuite l’API
  existante ; l’atelier local historique reste une sauvegarde indépendante.

## Parcours natif

Un bouton « Reprendre les données de ce PC » dans le mode local ouvre l’assistant
intégré. Le compte se connecte à son serveur HTTPS depuis Manager, sans terminal.
Le client vérifie d’abord une capacité de reprise versionnée : un serveur ancien
sans cette API est signalé comme à mettre à jour, sans contourner sa protection.
Le serveur de production actuellement épinglé devra être mis à jour séparément
avant tout usage réel de ce parcours.

1. Sauvegarder le PC dans un nouveau dossier privé LocalApplicationData, hors
   atelier et Nextcloud ; contrôler et restaurer cette sauvegarde à part.
2. Préparer l’archive portable avec les conversions déjà reconnues. Toute anomalie
   bloquante est visible, pas supprimée. Les fiches de collections sans dossier
   peuvent être conservées à part uniquement après sélection explicite ; conserver
   leurs octets, empreintes et identité dans l’annexe et l’archive originale.
3. Transférer par HTTPS authentifié en morceaux de 512 Kio, pour fonctionner même
   derrière un proxy à limite basse. Afficher octets transférés et annulation.
   Chaque morceau a son empreinte et son index ; un reçu identique est idempotent.
4. Préparer côté serveur hors de l’atelier actif et comparer avec un instantané
   cohérent de cet atelier. Afficher inchangés, PC seul, serveur seul et conflits.
5. Résoudre chaque conflit ; PC seul est ajouté, serveur seul conservé. Un choix
   concerne le fichier complet ; pas de fusion implicite de JSON, journaux ou 3MF.
   Les audits métier de la copie résultante doivent rester favorables.
6. Montrer le bilan, sauvegardes, choix et conséquences. Annuler reçoit le focus ;
   Entrée ne valide pas l’activation. Le PC est revérifié avant la demande finale.
7. Appliquer seulement si l’empreinte de l’atelier serveur n’a pas changé depuis
   le bilan. Une nouvelle modification invalide le plan et demande une comparaison.
8. Vérifier le reçu et l’état final. Après succès et contrôle de stabilité du PC,
   proposer explicitement l’ouverture du mode serveur, sans écraser la source PC.

La session expire au plus après huit heures ; le mot de passe n’est pas enregistré.
Une coupure conserve l’identifiant de reprise, le serveur, le compte et l’empreinte
locale dans un journal privé sans jeton. Réauthentification au même compte/serveur,
puis consultation de l’état distant avant de reprendre. Aucun nouveau Apply après
un résultat incertain. Un reçu appliqué ferme la reprise, sans importer deux fois.

## Comparaison et copie candidate

Les instantanés portent chemins relatifs validés, tailles, SHA256 et dossiers.
L’empreinte globale dépend des chemins/contenus, pas des dates filesystem.
Les collisions de casse Windows/Linux, fichier/dossier, chemins absolus, liens,
traversées et métadonnées incohérentes bloquent. Les choix sont liés aux deux
instantanés et invalidés si une source change. Les champs inconnus et documents
sont conservés sans réécriture générique.

La candidate est créée dans un nouveau dossier, jamais au-dessus de l’atelier.
Chaque copie de fichier est contrôlée contre l’empreinte annoncée. Le bilan
final contient aussi les éventuels renouvellements de versions techniques.
Les audits identifiants, références, liens clients/projets, chemins et collections
sont rejoués sur cette candidate ; aucun succès partiel n’est activable.

Les anciennes fiches de collections sans dossier ne sont pas exclues par défaut.
Le choix de conservation en annexe n’est valable que pour les fiches réellement
sans dossier et identifiées par GUID, jamais une liste de fichiers arbitraires.
La source PC et son archive originale contiennent toujours les fiches initiales.

## API privée et opérations durables

Routes sous /api/v1/workspace-migrations, authentifiées comme l’atelier. Toutes
les mutations cookie gardent la protection CSRF ; les clients natifs utilisent
leur jeton et les règles HTTPS existantes. Aucun import public, port, racine disque
choisie par le client ou confiance réseau supplémentaire.

- GET capabilities : version1, limites ; serveur ancien => arrêt explicite.
- POST operations : GUID de commande, taille et SHA256 d’archive ; création privée.
- GET operations/{id} : phase, morceaux acquis et empreintes, bilan/reçu sans secret
  ni chemin absolu serveur.
- PUT operations/{id}/parts/{index} : corps brut <=512Kio, SHA256 attendu, index borné.
- POST operations/{id}/prepare : recomposition et SHA256 global, restauration/audit,
  sauvegarde serveur cohérente et plan de comparaison.
- PUT operations/{id}/choices : choix bornés identifiés par chemins présents au plan.
- POST operations/{id}/apply : confirmation explicite et empreinte du plan révisé.

Les limites sont locales à ces routes : ne pas augmenter globalement les requêtes
métier de 8192 octets. Limites initiales : archive 4Gio, morceaux512Kio ; respecter
également les limites d’entrées/décompression et les vérifications ZIP existantes.
Chaque opération a son verrou, ses fichiers atomiques et ses états durables.
Les envois et décisions ne touchent pas l’atelier actif. Les états et archives
restent sous l’état privé du serveur ; jamais sous wwwroot.

## Activation et récupération

Le même verrou CommandStore protège lectures/écritures métier et activation.
Sous ce verrou, recontrôler l’instantané serveur, la candidate et sa sauvegarde.
Refuser d’activer si une commande est encore de résultat incertain.
Conserver une sauvegarde métier vérifiée de l’ancien atelier et garder son dossier.

La racine configurée doit rester la même pour les services existants : déplacer
l’ancienne racine vers un nouveau nom de sauvegarde privé puis la candidate vers
ce chemin, sur le même volume. Écrire et vider sur disque un journal avant chaque
phase. Le journal lie exclusivement les chemins dérivés de la racine configurée,
l’opération GUID et les empreintes auditées ; aucun chemin fourni par l’appelant.
Au redémarrage, récupérer avant de refuser une racine momentanément manquante :
restaurer l’ancien dossier ou confirmer une candidate complète par ses empreintes.
Un état impossible ou ambigu ferme le service métier, sans suppression aveugle.

L’opération Applied conserve un reçu durable. Répéter la même demande renvoie ce
reçu sans nouvelle activation ; un autre plan/identifiant est refusé. Après une
réponse perdue, le client ne fait que consulter ce reçu. Authentification, clés
et journaux de commandes sont hors atelier et ne sont pas remplacés.

## Vérifications exigées

Tests de comparaison par contenu, conflits explicites, conservation des fichiers
serveur seuls, snapshots périmés, audits, changements PC pendant préparation,
chemins/collisions/liens, archive altérée, idempotence des morceaux et Apply,
confidentialité/CSRF/limites, résultat perdu, redémarrages à chaque état de
bascule et refus de commandes périmées. Parcours natif Headless et tests serveur
Windows/Linux avec ateliers fictifs. Ne pas annoncer une migration réelle avant
mise à jour du serveur et confirmation du bilan par l’utilisateur.
