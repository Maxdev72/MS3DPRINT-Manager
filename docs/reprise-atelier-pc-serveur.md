# Reprendre les données du PC sur le serveur

Le PC peut contenir des données plus récentes que le site. Cet assistant prépare une copie,
compare les deux ateliers et demande un choix pour chaque conflit. Il ne remplace jamais
les données du PC. Il s’agit d’une reprise initiale vérifiée, pas d’une fusion automatique
permanente de deux ateliers modifiables séparément.

## Depuis Manager

1. Rester dans l’atelier local, ouvrir **Paramètres**, puis **Reprendre les données de ce PC**.
2. Se connecter au serveur HTTPS avec son compte habituel. Le mot de passe reste en mémoire.
   Si Manager demande une mise à jour du serveur, arrêter ici : aucune archive n’est transférée.
3. Lire le périmètre de sauvegarde. Les dossiers hors des dossiers gérés restent sur ce PC.
   Ils ne sont ni sauvegardés par cette opération, ni transférés. Leur exclusion doit être acceptée.
4. Laisser Manager sauvegarder puis vérifier l’original dans son dossier privé, hors Nextcloud.
   Les fiches de collections dont le dossier a disparu bloquent la préparation ; leur conservation
   en annexe inactive exige une sélection explicite. L’original reste dans la sauvegarde.
5. Transférer et comparer. Les fichiers uniquement PC sont ajoutés ; ceux uniquement serveur
   sont conservés. Pour chaque conflit, choisir le fichier entier du PC ou du serveur.
   Les dates sont affichées pour information, sans décider automatiquement de la bonne version.
6. Vérifier le bilan et cliquer explicitement sur **Activer cette copie sur le serveur**.
   Annuler garde le focus ; Entrée ne confirme pas l’activation.
7. Après vérification du reçu et de la stabilité du PC, choisir **Ouvrir le mode serveur**.
   Les pages Clients, Projets et Tâches utilisent alors le serveur.

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
