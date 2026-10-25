# Navigation fichiers et fluidité — 25 octobre 2026

## Parcours livré

- Avalonia, Desktop, Fluent et Headless passent de 12.1.3 à **12.1.4**. Le composant WebView conserve son cycle propre et sa référence 12.1.1. Les changements concernés figurent dans les [notes officielles](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.4).
- Les dossiers de projets et les dossiers ouverts depuis les collections partagent un navigateur : recherche par mots, accents/casse ignorés, filtre Modèles 3D / CAO / PDF / Images / Autres, effacement des filtres et actualisation. La recherche concerne **le dossier courant**, sans parcours récursif implicite.
- Le filtrage travaille sur l’inventaire déjà chargé. Un filtre de format conserve les dossiers accessibles ; une recherche textuelle s’applique aussi à leurs noms. Les résultats sont paginés par **50 lignes**, avant construction des contrôles, et un nouveau filtre revient à la première page.
- Les noms disposent d’une largeur contrainte et d’une ellipse. La miniature 3D passe à 40 px ; l’aperçu de 360 px est déclenché sur le bouton de ligne, qui reçoit effectivement le pointeur. Le cache, la limite de 40 Mio et l’annulation des miniatures détachées sont conservés. Un fichier trop lourd ou illisible conserve son icône et peut toujours être ouvert normalement si le visualiseur l’accepte.
- L’accueil présente six fichiers récents et permet d’en afficher jusqu’à vingt, avec réouverture et accès à leur dossier. Un fichier disparu reste signalé comme indisponible ; aucun document n’est supprimé ou déplacé par l’historique.
- Les actions existantes — import multiple, nouveau dossier, renommer, déplacer, ouvrir avec, corbeille confirmée — restent disponibles. Les raccourcis clavier et la réduction des animations existants sont conservés.

## Historique des fichiers

`RecentFileStore` enregistre au maximum 20 chemins relatifs et dates dans `.ms3dprint-manager/recent-files.json`. Le fichier est écrit par remplacement atomique sous verrou local, en relisant avant modification. Il suit l’espace de travail et reste réancrable lors d’une restauration ou d’un déplacement de l’espace.

Seuls les fichiers des onze dossiers métier gérés sont acceptés. Les chemins externes, traversées, métadonnées, liens et extensions exécutables sont refusés. Les fichiers ouverts en dehors de l’espace ne sont pas ajoutés. Un historique corrompu est signalé et conservé, jamais écrasé silencieusement.

Les documents sont enregistrés après ouverture interne ou transmission au mécanisme externe. Pour un modèle 3D, l’événement vient du chargement effectif du visualiseur, y compris son sélecteur et le glisser-déposer. Le système ne peut pas certifier qu’une application externe a ensuite terminé l’ouverture, ni qu’un choix a été confirmé dans le dialogue Windows « Ouvrir avec ».

Si l’espace change pendant le chargement d’un modèle, l’ajout du raccourci à l’ancien espace n’est pas garanti ; le fichier n’est pas attribué à un espace non correspondant. Les fichiers renommés ou déplacés ne sont pas recherchés automatiquement : leur ancien raccourci reste indisponible et une nouvelle ouverture crée le raccourci actuel.

## Protocole des mesures

Scénario `File_browser_measurements_record_loading_layout_and_realized_rows`, lancé explicitement avec `MS3DPRINT_PROFILE_FILES=before` ou `after`, compilation **Release**, environnement headless Avalonia/Skia. Espaces temporaires synthétiques uniquement ; aucune seconde instance de l’application utilisateur.

Avant : Avalonia 12.1.3 et anciennes listes. Après : Avalonia 12.1.4 et navigateur paginé. Mesures sur le même poste, runtime 10.0.13, Windows 10.0.26300.0, 28 processeurs logiques. Trois répétitions par taille ; résultats ci-dessous : médiane des trois. Le temps inclut lecture du dossier, construction des contrôles et mise en page ; l’allocation est le compteur géré global du processus pendant l’opération, **pas la consommation RAM résidente**.

| Entrées du dossier | Avant : chargement/mise en page | Après | Lignes créées avant → après | Allocations gérées avant → après |
|---:|---:|---:|---:|---:|
| 100 | 411,0 ms | 149,8 ms | 100 → 50 | 49,7 → 28,0 Mio |
| 1 000 | 2 345,9 ms | 720,1 ms | 1 000 → 50 | 557,3 → 29,1 Mio |
| 10 000 | Non retenu | 7 093,1 ms | Non retenu → 50 | Non retenu → 42,9 Mio |

Sur 1 000 fichiers, ce protocole mesure donc environ **3,3 fois moins de temps** et **95 % d’allocations gérées en moins**. Ce résultat concerne l’ensemble du lot ; il n’est pas attribué à la mise à jour Avalonia seule.

Après chargement, le filtrage suivi de mise en page mesure respectivement **33,6 / 23,3 / 14,9 ms** pour 100 / 1 000 / 10 000 entrées dans ce scénario. Ces valeurs dépendent aussi de l’échauffement du processus et du nombre de résultats affichés ; elles ne décrivent pas une loi de performance suivant la taille.

Le premier essai de l’ancienne interface à 10 000 entrées a été interrompu après observation d’une consommation mémoire excessive du processus de test. Aucun chiffre comparatif avant/après n’en est tiré. Le protocole initial conservé compare 100 et 1 000 ; 10 000 est un essai de capacité uniquement après correction.

**Limites importantes :** à 10 000 entrées, la lecture exhaustive des métadonnées et les vérifications de chemins prennent encore du temps. La pagination borne la charge de l’interface, pas le coût total d’énumération du disque. Ces mesures ne sont ni des FPS GPU, ni un test Nextcloud déconnecté, ni une mesure de latence d’un utilisateur sur un autre poste. Aucun gain de fluidité matérielle universel n’est annoncé.

Résultats bruts : `outputs/performance/files-before.json` et `files-after.json`. Journal initial borné : `outputs/files-before-bounded.log` ; journal final de mesure et des trois nouveaux parcours UI : `outputs/files-after.log`.

## Vérifications

Les 14 cas Core sur filtres et historique ont échoué avant implémentation (`outputs/files-core-red.log`), puis réussi. Trois parcours UI ont échoué avant intégration (`outputs/files-ui-red.log`) : dossiers génériques, dossiers projet et récents. Le dernier a été adapté pour attendre la fin du gestionnaire de clic asynchrone avant de vérifier le nouvel état.

Ils vérifient les filtres combinés, le focus de saisie, l’accès aux dossiers, les deux pages de 73 éléments, le retour à la première page, la réouverture et le signalement d’un fichier disparu. Captures 960/1600 px inspectées dans `outputs/qa/run-afe6aed03d6c4fc39cbfb7f0e3f4f2d7/drafts-duplication/files-*.png`.

La revue indépendante ciblée n’a identifié aucun défaut important ; la limite du changement d’espace pendant chargement 3D est documentée ci-dessus. La vérification globale et la livraison sont consignées dans le rapport de corrections.

## Complément — lecture progressive et retour au contexte

Le lot suivant remplace l’attente de l’inventaire complet par une lecture en arrière-plan. Le navigateur, sa recherche et son filtre apparaissent immédiatement. Les premiers résultats arrivent par lots de 50, puis 500 entrées maximum ; sur un stockage lent, un lot est transmis après environ 100 ms de lecture, même incomplet. Un appel système déjà bloqué sur le disque ne peut cependant pas être interrompu par ce mécanisme.

Le statut « Lecture du dossier… » identifie les résultats partiels ; un résultat absent pendant cette phase ne signifie pas que le fichier n’existe pas. La première page se remplit, puis reste stable pendant la lecture sans filtre. Le classement final conserve les dossiers en tête et l’ordre alphabétique ; les résultats filtrés peuvent s’enrichir pendant la lecture. La vue conserve au plus 50 lignes de fichiers à la fois.

Changer de page ou de dossier, revenir en arrière ou fermer la fenêtre annule la lecture précédente. Chaque retour de lot vérifie encore son identité et son espace avant de modifier l’affichage. Le verrou des boutons/clavier de l’historique se termine dès l’installation de la vue et ne couvre plus toute la lecture du disque.

La recherche, le format, la page et le défilement sont mémorisés en mémoire pour au plus 100 dossiers, propres à l’espace actif. Ils sont restaurés au retour ou à l’actualisation ; la page et le défilement sont bornés au contenu encore disponible. Cette mémoire ne survit pas à la fermeture du logiciel ; elle ne stocke pas une copie périmée de l’inventaire. L’actualisation relit les fichiers. En cas d’échec de lecture, les lignes partielles sont retirées de l’affichage, les filtres désactivés et un bouton Réessayer permet de reprendre.

L’énumération réutilise les métadonnées fournies par `DirectoryInfo.EnumerateFileSystemInfos`, au lieu de relire séparément attributs, date et taille de chaque entrée et tous ses ancêtres. Les racines et ancêtres sont contrôlés avant lecture ; les entrées avec point de réanalyse passent toujours par la vérification du type (Cloud Files autorisé, liens/jonctions refusés). Le navigateur ne lit pas le contenu des documents pour dresser la liste.

Tests supplémentaires : annulation au milieu de l’énumération, chemin extérieur et préannulation, navigateur disponible pendant la lecture, absence de mises à jour après départ, retour historique projet/générique avec filtres/page/défilement, actualisation après disparition de fichiers et reprise d’un dossier indisponible. Le test du retour historique a reproduit le verrou d’entrée avant correction. Journaux : `outputs/files-progressive-red.log`, `files-enumeration-red.log`, `files-retry-red.log`, `files-history-lock-red.log` et `files-history-lock-green.log`.

La mesure complémentaire utilise `MS3DPRINT_PROFILE_FILES=progressive`, trois répétitions par taille dans le même environnement Release headless. Elle ajoute un relevé de la première page après mise en page, observée au plus toutes les 10 ms ; cette valeur peut être une borne haute si la lecture se termine entre deux observations. Le temps total reste lecture, construction et mise en page. Résultats dans `outputs/performance/files-progressive.json`. Les mesures locales n’établissent pas de garantie sur un disque distant ou Nextcloud, ni une fréquence d’images réelle.

Médianes de la mesure finale (`outputs/files-progressive-final-measures.log`) :

| Entrées | Lot précédent, chargement complet | Lecture progressive, première page observée | Lecture progressive, chargement complet | Allocations gérées du nouveau lot |
|---:|---:|---:|---:|---:|
| 100 | 149,8 ms | ≤ 227,3 ms | 227,3 ms | 56,1 Mio |
| 1 000 | 720,1 ms | ≤ 135,4 ms | 280,7 ms | 56,7 Mio |
| 10 000 | 7 093,1 ms | ≤ 139,5 ms | 292,8 ms | 62,5 Mio |

Le gain concerne surtout les gros dossiers : environ 24 fois moins de temps total à 10 000 entrées dans ce protocole. À 100 fichiers, les deux mises en page (premiers résultats puis classement final) augmentent le temps total et les allocations. Ce compromis permet d’utiliser les premiers fichiers pendant une lecture lente ; il ne constitue pas une réduction générale des allocations. Le nombre de lignes présentes reste de 50, quel que soit le total. Les premiers essais du lot étaient plus rapides ; seuls ces chiffres de la dernière exécution sont retenus dans ce tableau.

## Sélection et rangement de plusieurs fichiers

Dans un dossier projet ou générique, cochez les fichiers à traiter. **Ctrl + clic** sur le nom ajoute ou retire un fichier ; **Maj + clic** sélectionne une plage dans l’ordre filtré courant, depuis le dernier fichier coché ou sélectionné. Ctrl + Maj ajoute la plage à la sélection existante. Au clavier, les cases fonctionnent avec Espace ; Ctrl/Maj + Espace fonctionnent aussi depuis le bouton d’ouverture d’un fichier. Le clic ordinaire sur son nom continue d’ouvrir le fichier.

La barre d’actions apparaît seulement lorsqu’au moins un fichier est sélectionné. Elle indique le nombre total, y compris ceux des autres pages. La sélection suit la pagination ; changer la recherche, le format, actualiser ou quitter le dossier la réinitialise. Les dossiers restent accessibles normalement et ne sont pas sélectionnables. Pendant une lecture partielle, les cases et raccourcis de sélection sont désactivés.

**Déplacer** ouvre une seule fois le choix du dossier de destination dans l’atelier. Les noms restent identiques ; un fichier déjà présent n’est jamais écrasé et provoque un échec pour cet élément. Aucun renommage automatique n’est effectué. **Corbeille** affiche obligatoirement le nombre, le dossier et les noms de tous les fichiers sélectionnés, puis demande confirmation. Annuler reçoit le focus ; Entrée ne confirme pas implicitement la corbeille.

Le logiciel gèle la liste et l’espace de travail avant de lancer le traitement en arrière-plan. Les opérations utilisent EntityManagementService, donc les protections des métadonnées et les journaux métier existants. Seuls des fichiers immédiatement dans le dossier source sont admis ; les doublons du lot sont ignorés. Un même lot ne peut pas être lancé deux fois simultanément. Les actions et filtres du navigateur source sont désactivés pendant le traitement, la navigation générale reste accessible et aucun résultat ne recharge une autre vue.

Chaque fichier constitue une opération indépendante : un échec n’annule pas les réussites et n’empêche pas de traiter les suivants. Le message final donne les nombres traités/en échec ; un panneau latéral détaille les noms et raisons des échecs, avec texte copiable. La liste source est relue si elle est toujours affichée. Les fichiers envoyés à la corbeille sont restaurables par le parcours habituel. Il n’y a pas de suppression définitive groupée ni de renommage groupé dans ce lot.

Vérifications : collisions sans écrasement, poursuite après échec, dédoublonnage, refus de dossiers et de fichiers non immédiats/disparus, restauration depuis la corbeille ; clics Ctrl/Maj réels simulés, ouverture ordinaire, pagination, effacement après filtre, annulation et confirmation de deux fichiers, choix d’une destination unique et bilan d’un déplacement partiellement réussi. Les espaces d’essai sont temporaires ; aucun document réel n’est modifié.

## Importer par glisser-déposer

Depuis un gestionnaire de fichiers, déposez un ou plusieurs fichiers locaux dans la zone d’import ou la liste du dossier affiché. La zone montre le nom de destination avant le chemin ; son infobulle et son nom accessible donnent le chemin complet. Pendant le survol accepté, le cyan et « Relâchez pour importer… » confirment le nombre de fichiers (doublons retirés). Le dépôt vise le dossier affiché, y compris lorsqu’il est relâché sur une ligne de sous-dossier : il n’ouvre pas une autre destination implicitement.

Dans un projet, le parcours existant propose le nom et, à la racine du projet, le classement. Dans un dossier générique, le nom d’origine est conservé. Les copies réussies sont suivies de la confirmation habituelle « Conserver la source » / « Supprimer la source ». Le dépôt indique toujours Copy au système, jamais Move : il ne donne pas au gestionnaire de fichiers l’autorisation de supprimer les originaux. Une collision n’écrase rien et n’ouvre pas de demande de suppression de source.

Les dossiers (même mélangés à des fichiers), textes, liens et éléments sans chemin local sont refusés. Un dépôt Move uniquement est refusé, plutôt que converti silencieusement en déplacement. Pendant le chargement du dossier, une lecture en erreur, une confirmation, un import ou une action groupée, les nouveaux dépôts sont refusés. La vue active et l’espace sont revérifiés au dépôt ; les chemins sont copiés en chaînes avant toute attente asynchrone. Le parcours existant arrête les fichiers suivants si la navigation change pendant l’import ; les copies déjà terminées restent conservées.

Le cache du survol contient seulement les chemins du transfert en cours (pas de lecture du contenu ni de contrôle du disque à chaque mouvement). Le Drop relit le transfert avant de démarrer, puis l’état visuel et le cache sont réinitialisés au dépôt, à la sortie et au détachement de la vue. Les boutons d’import existants restent disponibles.

Tests : événements DragOver/Drop routés Avalonia avec objets de stockage locaux synthétiques, dossiers projet et génériques, deux fichiers avec doublon, conservation des sources, refus d’un second dépôt pendant confirmation, rejet dossiers/mixte/texte/Move seul/vue périmée, collision et suppression explicite de source après copie. Le contrôle de clic Ctrl/Maj amène désormais la ligne dans la vue avant de cliquer, la nouvelle zone augmentant la hauteur du contenu. Ces tests headless ne constituent pas une manipulation réelle de l’Explorateur Windows ; aucune seconde instance de l’application utilisateur n’a été ouverte.

## Raccourcis dans la liste des fichiers

Lorsque le focus est sur une ligne, une case ou la pagination de la liste :

| Raccourci | Action |
|---|---|
| Ctrl+A | Sélectionner tous les fichiers du résultat filtré, sur toutes les pages ; les dossiers sont exclus. |
| Échap | Effacer la sélection, sans modifier les filtres. |
| F2 | Renommer l’unique fichier sélectionné avec la fenêtre habituelle ; aucune action si zéro ou plusieurs fichiers sont sélectionnés. |
| Suppr | Ouvrir la confirmation de corbeille pour les fichiers sélectionnés ; aucun effacement avant confirmation explicite. |

Les autres combinaisons (notamment Maj+Suppr) n’activent pas ces commandes. Dans la recherche ou un champ d’édition, Ctrl+A sélectionne le texte et Suppr modifie le texte normalement ; F2 n’y renomme aucun fichier. Le chargement, les erreurs de lecture, un import, un traitement groupé, une confirmation ou une vue inactive empêchent les commandes sur les fichiers.

Les commandes asynchrones sont protégées contre les répétitions pendant l’ouverture et le traitement du dialogue. Après annulation d’une corbeille, les contrôles sont d’abord réactivés puis le focus revient à l’élément de la liste ayant lancé la commande, si la vue est toujours active. Après rechargement ou changement de page, aucun focus n’est renvoyé vers une ancienne ligne.

Tests : 72 fichiers PDF filtrés sur deux pages parmi d’autres formats, sélection/désélection au clavier, F2 inactif pour la sélection multiple, édition native du texte de recherche, annulation puis renommage F2, annulation de Suppr avec focus restauré et relance immédiate sans refocus manuel, puis confirmation. Le test du retour de focus a échoué avant le correctif. Journaux : outputs/files-keyboard-red.log, files-keyboard-focus-red.log et files-keyboard-targeted.log.

