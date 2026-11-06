# Reprendre les données du PC sur le serveur

Le PC peut contenir des données plus récentes que le site. Cet assistant prépare une copie,
compare les deux ateliers et demande un choix pour chaque conflit. Il ne remplace jamais
les données du PC. Il s’agit d’une reprise initiale vérifiée, pas d’une fusion automatique
permanente de deux ateliers modifiables séparément.

## Depuis Manager

1. Dans l’atelier local, ouvrir **Paramètres → Espace et stockage → Connexion à mon
   atelier en ligne**, puis **Synchroniser cet atelier**.
2. Saisir son compte habituel et cliquer **Se connecter et préparer la synchronisation**.
   Manager sauvegarde le PC, vérifie la copie et la compare automatiquement au serveur.
   Les anciennes fiches de modèles réellement sans dossier sont conservées à part dans
   la sauvegarde privée. Les fichiers source restent intacts.
3. Si des versions diffèrent, utiliser **Garder la version du PC pour tous les conflits**
   quand le PC contient les bonnes données. **Choisir fichier par fichier** permet
   de conserver certaines versions serveur. Les dates ne décident jamais à votre place.
4. Lire le bilan, notamment les éléments qui resteront uniquement sur le PC, puis cliquer
   **Confirmer la synchronisation**. Le serveur est sauvegardé avant modification.
   Fermer conserve le focus ; Entrée ne confirme jamais cette modification.
5. Manager ouvre automatiquement le mode serveur après vérification du reçu et de la
   stabilité du PC. Clients, projets et tâches utilisent alors les mêmes données que le
   site. Ce premier envoi n’est plus à refaire pour les modifications réalisées dans ce mode.

Les détails des fichiers et de la comparaison restent accessibles dans des rubriques
repliables. Aucun chemin technique ni empreinte n’est requis pour utiliser l’assistant.
Les autres modules restent accessibles dans l’atelier local ; les fichiers modifiés
séparément sur le disque du PC ne sont pas encore synchronisés en continu.

## En cas d’interruption

Fermer conserve la sauvegarde et le journal de reprise. Rouvrir l’assistant avec le même
atelier, serveur et compte permet de retrouver l’opération. Après une demande d’activation,
Manager consulte uniquement son état : il ne renvoie jamais une activation incertaine.

Si le PC change depuis la sauvegarde, la copie ne peut plus être présentée comme son état
actuel. Si le serveur change pendant le bilan, l’activation est refusée. Garder les sauvegardes
et refaire une comparaison ; ne pas supprimer un journal incertain pour forcer une activation.

## Limites de cette version

- Archive transférée limitée à 4 Gio, envoyée en morceaux de 512 Kio. Le temps dépend du volume.
- Au maximum 10 000 conflits dans une comparaison ; un bilan plus grand est bloqué.
- Les choix portent sur des fichiers complets : pas de fusion champ par champ d’un même fichier.
- Les documents et modules non encore portés sont conservés. Leur édition à distance n’est pas
  ajoutée ici ; le mode serveur conserve les fonctions déjà disponibles dans Manager.
- Les comptes clients et ateliers multiples isolés restent un chantier distinct. Cette version
  utilise le compte propriétaire existant, sans inscription publique.
- Le serveur en production doit être mis à jour séparément avant de proposer cette fonction.
  La publication du logiciel PC seule ne modifie pas le site.

Mise à jour du 6 novembre 2026 : le compagnon ms3dforge.fr dispose maintenant de l’API de reprise. L’étape de mise à jour serveur indiquée ci-dessus est réalisée pour cet hébergement ; le transfert réel attend vos choix et votre confirmation dans Manager.

Les anciens dossiers projet présents uniquement sur le serveur peuvent être conservés
avec leurs fichiers sans inventer une fiche projet, un statut ou des tâches. Le bilan les
signale comme dossiers historiques. Cette exception ne s’applique pas à une fiche client
manquante ou corrompue, ni aux projets PC sans fiche : ces anomalies restent bloquantes.
Une ancienne comparaison bloquée uniquement sur ces dossiers serveur peut être revue
après mise à jour, sans renvoyer l’archive. Une modification des sources ou une activation
déjà engagée interdit cette reprise et conserve les protections habituelles.
