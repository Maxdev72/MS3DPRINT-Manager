# Journal des modifications

## 25 octobre 2026

### Application compagnon Android, version 1.1
- Application refaite en cinq écrans : Saisir (achat ou recette, appareil photo, galerie, PDF), Projets (recherche, filtres, bilan, saisie préremplie depuis un projet), Activité (chiffre d’affaires, mois par mois, seuils), Envois (état de chaque saisie, renvoi) et Réglages. Même palette que le logiciel PC, icône du cube MS3DPRINT.
- Le PC publie l’état de l’atelier (projets, clients, activité, seuils, fournisseurs récents) dans le dossier « Echanges mobile » ; le téléphone le lit sans jamais écrire dans les registres. Paramètres > Atelier > « Application mobile » : publication automatique toutes les 3 minutes, publication immédiate, ouverture du dossier, désactivation.
- Les recettes saisies sur le téléphone s’importent depuis le bouton « Téléphone » de la page Recettes, comme les achats depuis la page Achats ; le PC signale l’arrivée de nouvelles saisies et leur nombre dans l’en-tête des pages.
- Une facture reçue par e-mail se partage vers l’application : le PDF est lu sur le téléphone et préremplit la recette. Saisie en cours et photo sont conservées si Android ferme l’application ; envoi hors ligne avec renvoi automatique ; messages d’erreur en clair.
- Vérifiée sur émulateur Android 16 : script `tools/android/smoke.py` (12 vérifications) et parcours manuels détaillés dans docs/application-compagnon-android.md.

### Fiche projet plus compacte
- Le bilan du projet tient sur une seule ligne de chiffres (estimé, encaissé, reste à encaisser, achats liés, encaissé moins achats ; les achats et le net n’apparaissent que lorsqu’ils existent), et la liste des estimations liées est repliée par défaut : les fichiers du projet remontent dans l’écran.

### Application compagnon Android (première version)
- Nouveau projet `src/MS3DPRINT.Manager.Android` : une application Android pour noter un achat (date, fournisseur, libellé, montant, règlement, pièce, projet), photographier le ticket avec l’appareil photo (image redressée et réduite à 1600 px) et l’envoyer dans le dossier d’échange « Echanges mobile », que le PC importe ensuite. Le dossier est choisi une fois et mémorisé ; les derniers envois et les fournisseurs récents sont rappelés.
- Corrigée après essai dans un émulateur : plantage au démarrage (option d’allègement du code incompatible avec le moteur .NET Android, thème Android inadapté) ; l’envoi d’un achat avec photo a été vérifié de bout en bout et les fichiers sont lus par le PC. Poids ramené à 34 Mo.
- Hors de la solution principale (charge de travail Android et JDK requis). Mode d’emploi et limites : docs/application-compagnon-android.md.

### Correctif des notifications
- Les notifications (« Estimation enregistrée… ») retrouvent leur fond opaque : le texte des lignes situées dessous ne se superpose plus. La règle de style avait été perdue lors de la synchronisation du 25 octobre ; un test en vérifie désormais l’opacité.

### Échange avec l’application mobile : réception des achats
- Préparation de l’application compagnon Android : le téléphone dépose ses saisies (un petit fichier et la photo du ticket) dans le dossier « Echanges mobile » de l’espace de travail, synchronisé par Nextcloud. Il n’écrit jamais dans les registres.
- Page Achats > « Téléphone » : liste les saisies reçues, avec un récapitulatif dans l’en-tête de la page. Après confirmation, chaque achat entre dans le registre avec la photo comme justificatif ; une référence absente devient « Ticket du jj/mm/aaaa ». Un achat déjà importé n’est pas dupliqué, les fichiers traités passent dans le sous-dossier « Traites » et les fichiers incomplets (photo pas encore synchronisée) sont signalés.

### Rattrapage du livre, seuils d’activité et bilan par projet
- Page Recettes > « Factures » : le logiciel parcourt l’espace de travail, lit les factures PDF (Indy, ancien modèle, modèle FAC-AAAA-NNN) et liste celles qui n’ont pas de recette. Rien n’est coché par défaut : on coche les factures payées, on vérifie la date d’encaissement, la nature et le règlement, puis on enregistre. La facture reste à sa place et devient le justificatif de la ligne. Une même facture classée à deux endroits n’apparaît qu’une fois ; les factures mixtes (vente et prestation) se saisissent avec « Ajouter » pour indiquer la part marchandises.
- Paramètres > Atelier > « Seuils d’activité » : on saisit les plafonds de son régime (micro et franchise de TVA, total et prestations). La page Recettes affiche le niveau atteint pour l’année affichée, en alerte dès 80 % et à nouveau une fois le plafond atteint. Aucun montant légal n’est imposé par le logiciel.
- Fiche d’un projet > « Bilan du projet » : prix du dernier devis, encaissé, reste à encaisser, achats liés et encaissé moins achats, d’après les recettes et achats rattachés à la référence du projet.

### Livre de recettes et registre des achats : corrections et totaux
- Corriger une ligne : le menu « ⋯ » propose « Corriger » sur chaque recette et chaque achat. Le formulaire est pré-rempli et demande un motif. Rien n’est effacé : la correction est ajoutée au journal avec son motif, la ligne affiche les nouvelles valeurs (« corrigée »), compte dans les totaux avec le nouveau montant, et « Voir l’historique » détaille les valeurs d’origine et chaque changement. La date reste dans la même année ; pour changer d’année, annuler puis ressaisir. Une ligne annulée ne se corrige plus.
- Les exports CSV et PDF indiquent le statut « Corrigée » avec le motif.
- Registre des achats : nouvel export « Totaux annuels » (total payé par mois et pour l’année, achats annulés exclus).
- Lecture des factures : un troisième modèle de facture (« FAC-2026-006 », date, total HT sans TVA, destinataire) est reconnu. Les 65 factures de l’espace de travail de l’atelier sont désormais lues entièrement.

### Classement des fichiers enregistrés par les logiciels externes
- La fiche d’un projet repère les fichiers `.3mf` enregistrés par Bambu Studio ou OrcaSlicer ailleurs que dans le dossier 3MF du projet (par exemple à côté du STL ouvert) et propose de les classer dans le dossier prévu par le modèle de projet, avec le nom `nom__RÉFÉRENCE.3mf`. L’origine est reconnue par le contenu du fichier ; le STL source est indiqué lorsqu’il est retrouvé dans le projet.
- Rien ne bouge sans clic sur « Classer » ; « Ignorer » mémorise le choix. Un fichier modifié depuis moins de 15 secondes, ouvert par un autre programme ou d’une autre origine (export CAO) n’est jamais proposé. Aucun écrasement : un nom déjà pris reçoit un numéro.
- L’analyse se relance toutes les 10 secondes tant que la fiche du projet est affichée. Sur l’espace de travail de l’atelier, 147 des 171 fichiers `.3mf` sont reconnus comme des projets Bambu Studio.
- Conception : docs/superpowers/specs/2026-10-25-classement-fichiers-externes-design.md.

### Livre de recettes et registre des achats
- Nouvelle page « Recettes » : registre chronologique des encaissements, avec date d’encaissement, client, libellé, nature (marchandises, prestations ou mixte), mode de règlement, numéro de facture et projet. Un sélecteur d’année et le total encaissé sont affichés en tête.
- Le journal est en ajout seul (`.ms3dprint-manager/revenues/AAAA.jsonl`, inclus dans les sauvegardes) : aucune ligne n’est supprimée ni modifiée. Une recette erronée s’annule avec un motif obligatoire ; elle reste visible, barrée, et sort des totaux. Une ligne illisible est signalée sans masquer les autres.
- Exports de l’année : PDF imprimable paginé, CSV/Excel (séparateur point-virgule, cellules protégées contre l’interprétation en formule) et synthèse des totaux de déclaration par mois et par nature. L’en-tête (entreprise, entrepreneur, SIRET) se règle depuis la page.
- Un numéro de facture déjà présent est signalé sans bloquer.
- Import d’une facture Indy : un PDF glissé sur la page, ou choisi depuis le formulaire, est lu localement (numéro, total TTC, client, nature vente/prestation, date d’émission) pour pré-remplir la saisie. Rien n’est enregistré sans confirmation et la date d’encaissement reste à indiquer. Le PDF est copié dans le dossier Documents du client (ou à défaut dans le dossier de pièces du livre), sans écraser ni déplacer l’original, et s’ouvre depuis le menu de la ligne.
- Depuis la fiche d’un projet, « Enregistrer un encaissement » propose le client, la référence du projet et le prix de la dernière estimation.
- Nouvelle page « Achats » : registre chronologique des achats de matières et fournitures (date, fournisseur, libellé, montant, mode de règlement, pièce justificative, projet), obligatoire pour la vente de marchandises. Même principe que le livre : journal en ajout seul (`.ms3dprint-manager/purchases/AAAA.jsonl`, inclus dans les sauvegardes), annulation avec motif, justificatif PDF ou image copié dans le dossier de pièces, exports PDF et CSV avec l’en-tête de l’atelier.
- Paramètres > Atelier : nouvelle carte « Mon entreprise » (entreprise, entrepreneur, SIRET, adresse, e-mail, téléphone), reprise dans l’en-tête des PDF du livre des recettes et du registre des achats.
- L’import des factures lit aussi les factures d’acompte et l’ancien modèle de facture (« FACTURE N° 25-10-31 », date en toutes lettres) : les factures d’acompte et l’ancien modèle sont lus.
- Conception : docs/superpowers/specs/2026-10-25-livre-de-recettes-design.md.

### Raccourcis de la liste des fichiers
- Ctrl+A sélectionne tous les fichiers du résultat filtré, sur toutes les pages ; Échap désélectionne. F2 ouvre le renommage d’un seul fichier sélectionné et Suppr reprend la confirmation de corbeille groupée.
- Ces raccourcis sont limités à la liste : les champs de saisie et menus conservent leur comportement. Les confirmations, imports, traitements et vues inactives bloquent les commandes de fichiers ; Maj+Suppr n’ajoute aucune suppression définitive.
- Après annulation de la corbeille, le focus revient à l’élément de la liste qui a ouvert la confirmation, après réactivation des contrôles.

### Import par glisser-déposer
- Les dossiers projet et génériques acceptent un ou plusieurs fichiers locaux déposés depuis l’extérieur. Une zone indique la destination et se colore pendant un dépôt accepté ; le nom du dossier reste visible même si le chemin complet est tronqué.
- Le dépôt reste une copie et reprend les imports existants : choix du nom/classement dans les projets, noms d’origine dans les dossiers génériques, progression et confirmation séparée avant toute suppression des originaux.
- Les dossiers, textes/liens, contenus non locaux et effets de déplacement seuls sont refusés. Un autre import, une action groupée, une confirmation ou une ancienne vue ne peuvent pas recevoir un nouveau lot.

### Sélection et actions groupées sur les fichiers
- Les dossiers projet et génériques permettent de cocher plusieurs fichiers, ou de les sélectionner avec Ctrl/Maj + clic. Le clic habituel conserve l’ouverture du fichier. Une barre contextuelle indique le nombre sélectionné et propose Déplacer, Corbeille et Désélectionner.
- La sélection suit la pagination ; changer le texte de recherche, le format ou le dossier efface la sélection. Les dossiers ne sont pas inclus et la sélection est désactivée pendant la lecture partielle.
- Le déplacement groupé choisit un seul dossier de destination, sans écrasement. La corbeille groupée exige une confirmation avec nombre, noms et dossier des fichiers ; Annuler reçoit le focus.
- Les traitements se font en arrière-plan, fichier par fichier, avec les services métier et journaux existants. Un échec n’arrête pas les suivants ; un bilan explique les fichiers non traités et les conflits.

### Chargement progressif des dossiers
- Les premiers fichiers apparaissent pendant la lecture du dossier, avec un statut indiquant les résultats partiels. La lecture s’arrête au changement de page, de dossier ou à la fermeture ; les résultats d’une ancienne lecture ne remplacent pas la vue active.
- Le retour dans un dossier retrouve la recherche, le format, la page et le défilement pendant la session. Une actualisation relit le disque en conservant ce contexte et ajuste les pages devenues inexistantes.
- Les métadonnées sont réutilisées depuis l’énumération du dossier. Les contrôles des chemins et des liens restent actifs, les espaces réservés Cloud Files restent admis. Une lecture impossible propose Réessayer.

### Correctifs Avalonia et navigation fichiers
- Le framework Avalonia, Desktop, Fluent et l’environnement de test Headless passent à 12.1.4. Les parcours de saisie, focus, menus, navigation et visualiseur conservent leur couverture de non-régression.
- Les dossiers de projet et les collections partagent une recherche par mots et un filtre de format : modèles 3D, CAO, PDF, images et autres. Les filtres utilisent l’inventaire en mémoire ; les dossiers restent accessibles et les résultats sont paginés par 50 lignes.
- Les noms sont contraints à leur colonne ; les miniatures 3D sont plus lisibles et leur grand aperçu répond depuis la ligne du fichier. Les actions d’import, classement, ouverture et corbeille confirmée sont conservées.
- L’accueil permet de retrouver jusqu’à 20 fichiers récents propres à l’espace de travail, de les rouvrir et d’atteindre leur dossier. Les fichiers disparus restent signalés sans suppression de données.
- Mesures headless Release : sur 1 000 entrées synthétiques, médiane chargement/mise en page 2 345,9 → 720,1 ms ; allocations gérées 557,3 → 29,1 Mio. Limites et protocole : docs/navigation-fichiers-et-fluidite-2026-10-25.md.

## 24 octobre 2026

### Sauvegarde et restauration
- Une carte dans Espace et stockage permet de créer une archive ZIP des données métier et réglages atelier, avec documents et corbeille au choix. Le registre historique des codes clients est inclus ; les préférences propres au poste restent locales.
- La restauration vérifie l’intégrité des fichiers et présente un aperçu avant confirmation. Elle produit une copie dans un nouveau dossier, sans remplacer l’espace actuel ni changer automatiquement l’espace actif.
- La dernière sauvegarde réussie est affichée. Les opérations se font en arrière-plan avec protection contre les doubles lancements. Détails et limites dans docs/sauvegardes.md.

### Recherche et choix des filaments
- L’Atelier permet de rechercher par marque, nom, matière et couleur, puis de combiner cette recherche avec une famille. Les mots se combinent et les accents/casse n’empêchent pas la recherche ; un état sans résultat permet d’effacer les filtres.
- Les variantes CF, GF et ESD sont repérées dans les lignes, à côté du caractère abrasif enregistré et du prix au kilo. Les formulations anciennes et personnalisées restent accessibles.
- L’estimateur propose un sélecteur avec la même recherche et les mêmes familles, sans ajouter de champs permanents au formulaire. Le devis garde sa bobine et son prix tant que le nouveau choix n’est pas confirmé. La sélection affiche aussi matière, couleur et prix.

### Matières alignées sur le guide du site
- Les suggestions des fiches filament reprennent les 26 matières de la bibliothèque technique actuelle de ms3dprint.fr, dont les variantes CF, GF, ESD et les TPU 90A/95A. Source et liste détaillées dans docs/catalogue-matieres.md.
- Les matières historiques hors liste et les saisies personnalisées sont conservées à l’édition. Le texte saisi est pris en compte ; une matière vide est signalée dans le formulaire. Aucune modification des bobines, marques ou prix déjà enregistrés.

### Actions longues et parcours complet
- La duplication de projet et la restauration affichent l’opération en cours dans un bandeau discret. Le bouton concerné indique l’attente ; la navigation reste utilisable.
- Une restauration ne peut plus être lancée deux fois pour le même élément. Un conflit conserve les fichiers et libère l’action pour réessayer. La fin d’une restauration ne recharge plus la corbeille si l’utilisateur a quitté ce contexte.
- La lecture du client préalable à une duplication est déportée hors du fil UI. Les protections existantes des imports et de l’enregistrement des devis sont conservées.
- Un test de parcours relie création du client et du projet, import avec conservation de source, estimation liée, double clic d’enregistrement et rechargement du devis sans doublon ni changement de prix.

### Catalogues après une erreur de lecture
- Dans Projets et Clients, les filtres attendent une lecture réussie. Une erreur reste visible avec son action de reprise ; une modification différée du filtre ne réaffiche plus un ancien catalogue.
- Les filtres saisis sont conservés et redeviennent disponibles après une reprise réussie. Le sous-titre distingue le chargement d’une lecture indisponible.

### Lecture des catalogues de l’atelier
- Les fiches matières et imprimantes illisibles sont signalées avec leurs détails, sans masquer les références saines ni effacer les fichiers. L’avertissement reste présent lors du changement de page et distingue un catalogue illisible d’un catalogue vide.
- « Réessayer » relit le catalogue après correction ou synchronisation. Les erreurs globales des pages Projets, Clients, Matières et Imprimantes reprennent la même présentation avec détail technique masqué au départ.

### États vides et reprise après erreur
- Une catégorie vide propose « Voir toutes les collections » et réinitialise aussi la puce de filtre. Un dossier vide propose directement d’importer des fichiers.
- Les erreurs de lecture des collections, dossiers et de la corbeille présentent un message simple, une action « Réessayer » et un détail technique sélectionnable, masqué au départ.
- Les entrées illisibles de la corbeille sont signalées sans annoncer à tort une corbeille vide. Leurs fichiers sont conservés et les autres éléments restent restaurables.

### Progression et bilan des imports multiples
- Un bandeau fixe affiche le fichier en cours, son numéro dans le lot, l’étape et le nombre de fichiers traités. L’arrêt attend la copie en cours et conserve les imports terminés.
- La confirmation de suppression de source reste individuelle, avec conservation par défaut. « Conserver et arrêter l’import » arrête les fichiers suivants sans supprimer l’original.
- Le bilan reste consultable jusqu’à sa fermeture : fichiers importés, échecs et fichiers non traités. Les détails des erreurs peuvent être dépliés et copiés. Une collision conserve le fichier existant et la source, puis le lot continue.

### Priorités sur l’accueil
- Les drapeaux de priorité des projets sont visibles dans « À reprendre » et « À surveiller », avec les mêmes couleurs que dans la liste des projets. Les infobulles et noms accessibles précisent priorité, référence et échéance.
- La file conserve d’abord les retards et échéances à sept jours, puis fait remonter les priorités hautes avant les fabrications sans urgence proche. À date égale, la priorité départage les projets. Les projets terminés et archivés restent exclus.

### Lisibilité de l’accueil, formulaires et aperçu des documents
- Les échéances affichent le nom du projet sur deux lignes, sa référence séparément et le badge en dessous, pour distinguer les projets proches sans survol.
- Les champs des formulaires exposent leurs unités et aides aux outils d’accessibilité. Une validation refusée ramène au champ concerné et affiche son erreur à proximité ; la correction efface ce message. Les résumés d’erreur utilisent une annonce non interruptive.
- La barre d’outils des documents passe sur plusieurs lignes en fenêtre étroite. Les commandes de zoom et de navigation ont un nom accessible explicite. Les images remplacées ou reçues après fermeture sont libérées.

### Vignettes, rendu 3D logiciel et téléchargements temporaires
- Lecture et décodage des vignettes en cache effectués en arrière-plan, avec le même plafond de concurrence que les nouveaux rendus. Une vignette corrompue est reconstruite ; une demande annulée ne réutilise pas le cache pour réafficher une ancienne image.
- Les cellules libèrent leurs images et annulent leurs demandes lorsqu’elles sont masquées ou détachées. Un résultat tardif est libéré au lieu d’être appliqué à une ancienne page.
- Le rendu 3D logiciel libère ses anciennes images au changement de résolution et à la sortie. Les rendus dépassés sont ignorés ; le retour au visualiseur conserve le modèle et la caméra.
- Les deux phases de nettoyage du tampon respectent les fichiers à conserver, les téléchargements actifs et les liens de dossiers. Les extractions et fichiers déjà annoncés restent protégés pendant l’attente de la fenêtre de téléchargement. Leur protection temporaire est levée à la fermeture de cette fenêtre.

### Fluidité des listes et des brouillons
- Projets, clients, collections, filaments et imprimantes affichent au maximum 50 lignes à la fois. Les boutons Précédent/Suivant et le compteur apparaissent uniquement sur les grands catalogues. Recherche et tri s’appliquent avant pagination ; une édition conserve la page tant que le filtre et le tri ne changent pas.
- La sauvegarde automatique capture les saisies puis écrit en arrière-plan. Les révisions déjà sauvegardées ne sont plus réécrites lors de chaque transition. Les écritures et retraits sont coordonnés pour empêcher un ancien travail de remplacer une saisie récente ou de recréer un brouillon consommé.
- La fermeture et les changements explicites de formulaire attendent toujours la dernière sauvegarde nécessaire. En cas d’échec, le formulaire reste protégé ; l’atelier indisponible n’est pas recréé.

### Suggestions clients et historique des devis
- Suggestions d’entreprises et d’adresses aux couleurs bleu nuit : survol cohérent, nom plus lisible et SIRET/adresse séparés. La sélection continue de compléter la fiche.
- Historique complet avec recherche par nom, client ou référence projet, insensible aux accents et à la casse. Affichage progressif par groupes de 20 ; les devis plus anciens restent accessibles.
- Création des devis par fichier temporaire, écriture vidée sur disque puis publication atomique sans écrasement. Une erreur de sérialisation ne laisse plus de devis partiel visible.
- Révision des devis vérifiée sous verrou avant modification : un ancien formulaire ne peut plus écraser une sauvegarde récente. En cas de conflit, les saisies restent dans le formulaire et le brouillon, et l’historique est actualisé pour recharger la version récente. La corbeille utilise le même verrou. Les anciens devis conservent leur moteur et leurs montants.

### Audit — premier lot de corrections de fiabilité
- Un atelier temporairement indisponible conserve son chemin. L’application propose de réessayer, de choisir explicitement un autre dossier ou de fermer ; elle ne bascule plus silencieusement sur le dossier historique. Un brouillon non sauvegardé reste protégé et son dossier absent n’est pas recréé par la sauvegarde automatique.
- Les réponses de chargement périmées sont ignorées pour les listes, les détails clients, les paramètres, l’accueil, l’inventaire de l’estimateur et son historique. Les contrôles vérifient le dossier actif, le dernier chargement demandé et, selon la page, la navigation.
- Les devis incomplets ou à l’identifiant incohérent sont écartés sans supprimer leur fichier. L’historique signale les fichiers illisibles et conserve les montants historiques. La mise à la corbeille d’un devis sain ne dépend plus de la lecture des autres devis.
- Les panneaux latéraux gardent le focus avec Tab et Maj+Tab, protègent les raccourcis de l’arrière-plan et rendent le focus à leur déclencheur. Les confirmations destructives conservent Annuler comme choix initial.
- Tests de régression : atelier déconnecté et reconnexion, réponses d’anciens ateliers dans les listes et détails, devis JSON incomplets, suppression en présence d’un autre JSON corrompu, navigation clavier et retour du focus.

## 23 octobre 2026

### Paramètres plus lisibles
- La recherche fait défiler directement jusqu’au libellé trouvé et repère les correspondances en cyan. Le résultat courant est souligné ; Entrée et Maj+Entrée parcourent les résultats sans quitter le champ de recherche. Effacer retire les repères et rétablit le haut de la rubrique ; une ancienne requête ne déclenche pas de défilement tardif.
- Recherche dans toutes les rubriques, insensible aux accents et à la casse, avec état sans résultat et effacement par bouton ou Échap. Effacer la recherche retrouve la rubrique précédente sans recharger ses contrôles.
- En-têtes de sections avec icônes, descriptions et actions mieux alignées ; adaptation des boutons à la largeur disponible, navigation latérale ou onglets selon la taille de fenêtre.
- Atelier regroupé en prix et rentabilité, production, cotisations et frais commerciaux, puis machines et matières. Les taux configurés et les postes désactivés sont explicitement visibles ; aucun changement des paramètres enregistrés.
- Le raccourci depuis l’estimateur ouvre directement l’atelier, même après une recherche dans une autre rubrique. Vérifications clavier, filtrage, retour à la rubrique et rendu normal / compact.

### Brouillons automatiques et duplication
- Sauvegarde des estimations en cours après 600 ms de pause et avant fermeture, avec indication d’état et reprise automatique du dernier brouillon de l’atelier. Les saisies incomplètes ou invalides, les fournisseurs, les affectations machine et les réglages sont conservés.
- Plusieurs brouillons distincts des devis enregistrés, accessibles dans l’historique. Chaque espace possède ses brouillons ; enregistrer un devis consomme uniquement son brouillon. Abandon et suppression demandent confirmation.
- Une erreur d’écriture conserve le formulaire et bloque son remplacement ou la fermeture. Les fichiers de brouillon sont remplacés atomiquement ; une erreur n’écrase pas la dernière sauvegarde complète.
- Icône « Dupliquer » dans l’historique : conserve les paramètres et les montants, prépare une copie indépendante, puis crée une nouvelle identité lors de l’enregistrement. Les snapshots des matières retirées et le moteur historique sont conservés.
- « Dupliquer le projet » dans les actions de liste et la fiche : nouveau nom, référence, statut À faire, échéance vide, description / notes / priorité reprises. Copie facultative des fichiers et sous-dossiers, sans écrasement ni suppression des originaux ; devis enregistrés non copiés.
- Contrôles des chemins et liens avant copie ; une création interrompue est conservée dans la corbeille de récupération. Tests des reprises, changements d’atelier, écritures verrouillées, confirmations, copies et erreurs de copie.

### Estimateur sur une seule page
- Suppression des onglets Saisie / Vérification et du code de navigation associé. Nom et projet côte à côte, données du slicer et capacité du plateau visibles dans la saisie principale.
- Prix actualisé pendant la saisie, prix commercial et remise directement dans la synthèse. Coûts, prix plancher et tableau de rentabilité restent repliables.
- « Enregistrer le devis » disponible en permanence dans le pied de la synthèse, avec raccourci Ctrl+Entrée. Validation et recalcul immédiats avant enregistrement ; les résultats historiques restent conservés sans modification.
- Protection contre le double-clic et la répétition du raccourci : après enregistrement, une nouvelle saisie, l’ouverture d’un devis ou la réinitialisation réarme la sauvegarde.
- Frais complémentaires accessibles via « Ajouter des frais » ; libellés poids / durée adaptés au périmètre et résumé de la production en pièces, plateaux, grammes et heures.
- Vérifications du calcul automatique, de l’enregistrement direct, des plateaux, des saisies invalides et du rendu en fenêtre large et compacte.

### Finitions visuelles et mouvement
- Survol continu et focus clavier des lignes métier, y compris les actions rapides ; même palette dans les recherches, champs, menus et accordéons.
- Ouverture des menus sur 160 ms et de leur opacité sur 120 ms ; la recherche apparaît une seule fois à l’ouverture, sans rejouer son animation à chaque saisie.
- Assombrissement progressif du fond à l’ouverture d’un formulaire sur 180 ms. Champs et commandes immédiatement disponibles, sans attente liée aux animations.
- Réduction des animations respectée également dans les menus détachés et le panneau latéral, avec vérifications automatisées de l’ouverture, fermeture et réouverture.

### Priorités des projets
- Quatre niveaux enregistrés : haute (rouge), moyenne (ambre), basse (bleu) et aucune (drapeau vide). Choix dès la création, dans la fiche et par clic sur le drapeau dans la liste.
- Filtre de priorité combinable avec le statut et le texte ; tri haute ou basse d’abord, projets sans priorité toujours à la fin. Effacement des filtres réinitialisant également la priorité.
- Anciennes fiches sans ce champ lues sans migration destructive ; modifications de statut, description, déplacement et autres informations conservant la priorité. Contrôle de version pour éviter d’écraser une modification concurrente.
- Tests de sauvegarde, ancien JSON, priorité invalide, création, édition annulée, retrait de priorité, filtres combinés et deux sens de tri ; captures en fenêtres normale et compacte.

### Import de plusieurs fichiers
- Sélection multiple depuis un projet ou un autre dossier. Parcours actuel conservé pour chaque fichier : nom et classement dans le projet, copie sans écrasement, puis confirmation distincte pour conserver ou supprimer sa source.
- Progression « fichier X sur Y » et commande « Arrêter l’import » dans le formulaire de nommage ; les imports déjà terminés restent conservés. Résumé du nombre importé et des échecs en fin de lot.
- Un conflit ou un fichier illisible conserve sa source et permet de traiter les suivants ; un seul lot à la fois, arrêt si la page ou l’espace de travail change. Aucun rafraîchissement tardif ne remplace le nouveau dossier affiché.
- Tests de deux imports successifs, nom d’origine / nom proposé, conservation / suppression confirmée des sources, arrêt partiel et conflit suivi d’un import réussi.

### Choix du fichier source après import
- Après chaque import réussi dans un projet ou un autre dossier, choix explicite entre conserver la source et la supprimer définitivement. Les chemins de l’original et de la copie importée sont affichés, y compris en cas de renommage.
- Conservation sélectionnée par défaut ; fermeture, Échap et absence de confirmation conservent l’original. Un import refusé ou échoué ne déclenche aucune suppression.
- Avant suppression : contrôle du fichier de destination, protection des métadonnées et des liens, comparaison des longueurs et empreintes SHA256. Sous Windows, la source est verrouillée contre les modifications et remplacements, puis supprimée par le même handle vérifié.
- En cas de verrouillage ou de changement de contenu, conservation des fichiers et message explicite. Tests du choix utilisateur, de la copie renommée, des fichiers différents, copie manquante/verrouillée et remplacement externe bloqué.

### Recherche et navigation plus fluides
- Recherche globale sur saisie ou collage, temporisée et annulable. États explicites de chargement, aucun résultat et lecture indisponible ; résultats plus larges, noms sur deux lignes et références / clients séparés.
- Ouverture directe des fiches clients depuis les résultats ; retour arrière vers la page de départ, sans passage par une liste jamais visitée.
- Premier résultat sélectionné, flèches pour parcourir, Entrée pour ouvrir, Échap et clic extérieur pour fermer. Bouton d’effacement et Ctrl+K pour remplacer rapidement la recherche.
- Annulation des résultats en cours lors de la navigation, de l’ouverture du visualiseur ou d’un dossier et de la fermeture de la fenêtre.
- Historique de devis limité à 112, 168 ou 220 px selon la hauteur disponible, avec marges compactes alignées ; davantage d’espace pour saisir dans les petites fenêtres.
- Vérifications des cas vides, texte collé, annulation, accès client, retour arrière et rendu en fenêtres normale et compacte.

### Menus d’actions harmonisés
- Menus « ⋯ » plus lisibles : fond bleu nuit, largeur minimale de 216 px, lignes de 42 px minimum, marges et icônes alignées.
- Survol et focus cohérents avec la palette ; suppression du survol gris natif. Actions de corbeille séparées, avec texte et icône rouges et fond de survol dédié.
- Même composant sur les clients, projets, fichiers, dossiers, collections, imprimantes et filaments. Fermeture à la sélection et confirmation des suppressions conservées.
- Tests des couleurs de survol, séparation de la corbeille, rendu normal et compact et ouverture du formulaire après sélection.

### Confirmation obligatoire des suppressions
- Audit des envois à la corbeille : clients, projets, collections, fichiers, dossiers, imprimantes, filaments et devis demandent une confirmation avant modification.
- Confirmation ajoutée aux retraits de postes de coûts, affectations de machines, dossiers du modèle de projet, comparaison 3D et rétablissement de l’arborescence standard.
- Annuler reçoit le focus dans les confirmations destructives. Échap annule ; une simple pression sur Entrée en dehors du bouton de confirmation ne valide plus la suppression.
- Tests de confirmation explicite et d’annulation, avec conservation des postes et affectations tant que le retrait n’est pas accepté.

### Menus déroulants et choix du projet
- Suppression de la limite de 620 px qui centrait les projets dans les grandes listes. Textes alignés à gauche, retours à la ligne des noms longs et largeur du menu limitée au champ.
- Palette des menus natifs harmonisée avec l’application : fond bleu nuit, survol et sélection bleus, états pressés et textes contrastés.
- Contrôle du menu réellement ouvert avec plusieurs projets en fenêtres de 960, 1 440 et 1 800 px, et test de la couleur et de l’alignement des éléments affichés.

### Historique sous l’estimateur
- Intégration à plat : mêmes bords et même fond que les panneaux de saisie, suppression du cadre interne, titre renforcé. Chaque ligne aligne le nom, la date d’enregistrement, le montant et « Reprendre » ; suppression toujours accessible séparément.
- Historique des estimations déplacé sous le formulaire et le panneau de prix, sur toute la largeur. Section repliable et liste défilante pour garder de la place à la saisie.
- Réouverture et mise à la corbeille conservées ; colonne de droite réservée au prix et à la rentabilité. Contrôle des fenêtres de 1 400 et 960 px et de la reprise d’un devis enregistré.

### Identifier le devis dès le début
- Largeur initiale de fenêtre et plafond de contenu portés à 1 400 px ; adaptation aux petites fenêtres et dimensions personnalisées conservées.
- Nom du devis et projet associé toujours visibles au début de la saisie, avant la prestation ; remise et cotisations regroupées séparément dans « Prix et remise ».
- Choix de projet sur deux lignes : nom du projet, puis référence et client. Même présentation conservée après sélection et infobulle complète.
- Vérification du placement, du lien au projet et de l’affichage en fenêtres normale et compacte.

### Présentation commune à l’atelier
- Style de l’estimateur étendu à l’accueil, aux listes, aux fiches et aux paramètres : surfaces plus sobres, titres hiérarchisés, filtres soulignés et lignes plus lisibles.
- Noms longs sur deux lignes, actions de fiche client adaptables à la largeur, notes repliables et coordonnées séparées pour une lecture rapide.
- Résumés du parc, des matières et de la corbeille fondés sur les données chargées ; rubriques des paramètres avec icônes et commande de modification alignée au titre de l’atelier.
- Formulaires communs agrandis, barres d’outils du navigateur et du visualiseur harmonisées ; contrôles visuels en fenêtres normale et compacte et 819 tests réussis.

### Fabrication sous-traitée
- Option « Fabrication confiée à un fournisseur » : quantité, coût d’achat total, transport fournisseur et nom facultatif. Les frais de préparation, finitions et livraison au client restent dans les compléments.
- L’achat externe remplace matière, machine et énergie internes. Les aléas d’impression internes ne sont pas appliqués à une commande fournisseur de pièces conformes. Protection du moteur contre le double comptage.
- Coût fournisseur distinct dans le détail, prix plancher et conseillé après charges et marge configurées, remise et alerte de déficit ; fournisseur et hypothèses sauvegardés avec le devis.
- Tests des quantités, frais par pièce, validation, absence de filament, conservation du prix enregistré au chargement et retour à une fabrication interne.

### Refonte visuelle de l’estimateur
- Éditeur sans contour, onglets avec repère mobile, champs agrandis et panneau de prix renforcé : prix final, prix unitaire, résultat et marge hiérarchisés.
- Décomposition du coût corrigé en matière, machine, énergie, travail, autres frais et échecs ; barre proportionnelle et correspondance au survol avec les montants de la légende.
- Vérification présentée comme une synthèse de devis, avec remise et total de commande ; détails de production et rentabilité repliables.
- Prix conservé pendant le bref recalcul avec un état explicite d’actualisation ; résultats effacés en cas d’erreur. Transitions natives interrompables, chiffres immédiats et réduction des animations respectée.
- Calculs, frais totaux, devis historiques et sauvegardes conservés ; contrôles des états zéro, remise sur prix manuel, validation et fenêtres compacte et normale.

### Frais complémentaires simplifiés
- Une seule règle de saisie : montants totaux pour la commande, sans multiplication supplémentaire par quantité ou plateau. Libellés courts : conception, main-d’œuvre, finitions, consommables, emballage / livraison, autres frais.
- Les choix « par commande / pièce / passage » sont remplacés dans l’atelier et les prestations supplémentaires par « à refaire si l’impression échoue ». La provision pour échecs reste distincte ; le forfait par pièce conserve son fonctionnement.
- Conversion à l’affichage des anciens montants unitaires V2 en totaux équivalents, sans modifier le prix enregistré. Format de sauvegarde explicitant les totaux, avec lecture compatible des anciens devis.

### Saisie numérique
- Alignement à gauche des champs numériques de l’estimateur et de l’atelier pour garder les chiffres et le point d’insertion au même endroit pendant la saisie.

### Navigation arrière / avant
- Boutons latéraux de souris et Alt + flèche gauche / droite pour retrouver les pages précédentes et suivantes, les fiches clients, projets, dossiers et le visualiseur.
- Historique limité à la session et à l’espace de travail courant ; une nouvelle destination remplace la branche d’avance, comme dans un navigateur.
- Retour aux fiches avec relecture des données actuelles, conservation du brouillon d’estimation et blocage de la navigation tant qu’un formulaire est ouvert ou qu’un retour est en cours.

### Nettoyage des champs de l’estimateur V2
- Fermeture des menus d’actions après sélection pour éviter leur superposition aux formulaires ; taux manuel affiché sur six décimales maximum sans arrondir le taux enregistré tant qu’il n’est pas modifié.
- Fiche imprimante : suppression de la durée de vie historique et du volume non utilisé. Un seul calcul automatique par années et heures productives annuelles ; taux manuel affiché uniquement lorsqu’il est activé.
- Correction de l’enregistrement des imprimantes avec amortissement annuel sans durée historique. Validation limitée aux paramètres actifs et coût horaire affiché en français.
- Anciens profils proposés en taux manuel à coût horaire conservé, sans inventer leur production annuelle ni modifier la pondération du parc ; anciennes données et devis préservés. Suppression du contrôle interne de sélection du moteur, V2 par défaut et conversion des anciens devis toujours explicite.

### Estimateur : saisie rapide et composition structurée
- Deux écrans : saisie, puis vérification et enregistrement. Accès direct aux données utiles, prix et rentabilité visibles pendant la saisie.
- Trois zones alignées avec icônes : prestation, données d’impression, compléments. Frais, options de fabrication et informations du devis regroupés dans trois accordéons.
- Durée saisie en heures et minutes, conversion décimale sans arrondi intermédiaire et restauration exacte des durées enregistrées ; validation des minutes inférieures à 60.
- V2 d’office sur les nouvelles estimations, suppression du menu de choix. Conversion explicite uniquement pour les devis historiques, sans modification de leur prix enregistré avant sauvegarde.
- Nom automatique facultatif, accès compact à l’ajout de filament, devis enregistrés repliables et raccourci Ctrl+Entrée pour vérifier puis enregistrer.
- Erreurs dans le formulaire et retours de focus ; correction du blocage sur une quantité d’impression masquée en modélisation seule.
- Transitions natives courtes des sections et du parcours ; préférence de réduction des animations respectée.

### Lisibilité et ergonomie des réglages
- Navigation des paramètres en colonne sur grand écran, en onglets sous 1250 px ; descriptions de rubriques et retour en haut lors d’un changement.
- Atelier présenté en lignes libellé / valeur, avec unités explicites, distinction marge / majoration et coût réel / supplément commercial.
- Bouton principal de modification, liens expliqués vers les imprimantes et filaments ; texte des paramètres agrandi et interligne augmenté.
- Aides de l’estimateur en 14 px ; noms de projets et de clients sur deux lignes, lignes de projets plus hautes pour limiter les troncatures.
- Captures des quatre rubriques en fenêtres normale et compacte ; vérification du changement de rubrique et de la position de défilement.

### Accueil et listes de travail
- File « À reprendre » limitée aux projets actifs : retards, échéances du jour, échéances à sept jours, puis fabrications en cours. Les projets terminés et archivés restent dans les listes de consultation.
- Chaque ligne indique la prochaine action, le statut et l’échéance ; noms complets disponibles au survol et libellés accessibles pour l’ouverture.
- Raccourci de modification dans les lignes de projets, visible au survol, au focus clavier et en fenêtre compacte, à côté du menu d’actions.
- Action « Effacer les filtres » dans les recherches sans résultat des projets et clients.
- Défilement de la colonne d’attention en petite fenêtre ; captures des quatre rubriques de paramètres.
- Tests du tri des priorités, de l’exclusion des archives et de la remise à zéro des filtres.

## 22 octobre 2026

### Parcours d’estimation et fiche projet
- Interventions et frais facultatifs regroupés dans une section repliable ; rappel du nombre de postes renseignés. Le repli ne change aucun montant du calcul.
- Accès direct aux coûts de conception pour les prestations de modélisation ; détail machine et énergie séparé des données essentielles du slicer.
- Synthèse de production en passages, grammes et heures machine ; confirmation après enregistrement d’une estimation.
- Fiche projet structurée autour du suivi, de l’échéance modifiable, des notes internes, des estimations liées et des fichiers.
- Préparation d’une estimation directement depuis un projet, avec liaison et nom préremplis ; consultation des devis valides même lorsqu’un autre fichier de devis est illisible.
- Tests de parcours et captures de la fiche projet en fenêtres normale et compacte.

### Identité MS3DPRINT et animations natives
- Palette du site ms3dprint.fr : bleu nuit, bleu électrique et cyan ; logo officiel et titres Bricolage Grotesque embarqués, sans chargement réseau.
- Accueil recomposé autour de « De l’idée à la pièce. », des actions de création et des étapes cliquables de production.
- Identité commune aux pages, listes, formulaires, paramètres et estimateur : icônes de rubrique, titres expressifs, surfaces plus nettes et accents contrastés.
- Repère de navigation animé, entrées de pages directionnelles, panneaux coulissants, réactions des boutons et confirmation discrète du prix recalculé.
- Animations courtes et interrompables ; option de réduction des animations respectée dans la fenêtre principale et les dialogues. Les montants restent immédiatement lisibles.
- Licence de la police et provenance des ressources accessibles dans Paramètres → Application → Identité visuelle et licences.
- Captures réelles des écrans en formats normal et compact, aperçu animé et test de navigation rapide sans instance native supplémentaire.

### Lisibilité et finition de l’interface
- Modernisation commune à toutes les pages : palette bleu nuit plus lisible, titres 30 px, navigation organisée en Activité / Atelier / Outils et nom réel de l’espace de travail.
- Projets, clients, filaments, imprimantes, collections et corbeille présentés sur une surface de travail cohérente ; en-têtes contrastés et lignes de 60 px. Projet présenté par son titre puis sa référence.
- Filtres des projets et clients placés sur une rangée dédiée ; actions de création explicites et recherche adaptée aux fenêtres compactes.
- Paramètres organisés en Atelier, Organisation, Espace et stockage, Application. Le raccourci de l’estimateur ouvre directement Atelier.
- Tous les formulaires DialogWindow bénéficient d’un en-tête et de boutons fixes, d’une fermeture explicite et de champs mieux espacés ; labels accessibles ajoutés par FormKit.
- Formulaire d’estimation et synthèse séparés : prix plancher, conseillé, final, résultat et marge visibles sur des lignes distinctes ; alerte dédiée sous le seuil de rentabilité.
- Champs numériques agrandis et alignés à droite, libellés plus contrastés, aides aérées et récapitulatif structuré en production, coûts et rentabilité.
- Réglages d’atelier décrits sous chaque section, intitulés longs des listes sur plusieurs lignes et compatibilité historique rangée dans une section dédiée.
- Thème sombre appliqué aux composants natifs et textes de recherche / espaces réservés plus contrastés. Petits libellés partagés agrandis.

### Paramètres de l’atelier, barèmes 2026 et aide
- Paramètres réutilisables regroupés dans **Paramètres → Atelier** : cotisations, CFP, commissions, kWh, marge, échec, forfait, frais généraux et natures des postes.
- Profils France 2026 sans ACRE, hors Cipav ou BNC Cipav, issus du catalogue annuel JSON et modifiables. Aucun régime n’est imposé aux autres utilisateurs.
- Préférences dans les métadonnées de l’espace de travail ; isolation entre ateliers, sans recalcul des anciens devis.
- Devis allégé : catégories de recettes par sélection simple et pourcentages uniquement en mode mixte. Les coûts rappellent leur nature dans leur libellé ; les montants historiques sont indiqués comme totaux de commande.
- Guide intégré « Comprendre chaque champ », aides au survol et aide accessible pour les champs principaux.

### Estimateur : thème et parcours de saisie
- Sections repliables harmonisées avec les surfaces bleu nuit, les bordures et les couleurs de survol / focus MS3DPRINT ; suppression des fonds noirs Fluent et des aplats derrière les titres ouverts.
- Titres accompagnés d’un descriptif court, chevrons explicites, ouverture au clavier et libellés accessibles.
- Champs essentiels présentés avant les options avancées ; marge, échec et remise regroupés avant les sections de configuration.
- Saisie désactivée pour les paramètres sans effet : capacité en mode une pièce, supports déjà compris dans le poids, charges désactivées et taux de référence remplacé par des affectations machine. Les valeurs restent conservées.
- Correction d’une course détectée par les tests dans la progression de l’orientation 3D : les notifications parallèles sont ordonnées jusqu’à 100 %.

### Estimateur V2 : production, charges et rentabilité
- Nouveaux devis calculés en V2 ; les devis déjà enregistrés conservent explicitement le moteur historique et leur résultat. Conversion uniquement en choisissant V2 dans l’assistant.
- Amortissement par imprimante : prix / (années × heures productives annuelles), taux manuel et maintenance séparée. Sans machine précise, moyenne pondérée du parc actif, jamais somme des taux.
- Pièces, ensembles et plateaux successifs ; affectations sur plusieurs machines avec leurs heures, taux et puissances propres. Supports et purges ajoutés uniquement si absents du poids slicer.
- Coûts classables par commande, pièce conforme ou tentative. Les échecs ne répètent que les postes par tentative ; la conception, l’expédition et les finitions conformes ne sont plus automatiquement majorées pour les échecs.
- Marge cible après charges sur le chiffre d’affaires, avec maintien du mode majoration. Forfait réel intégré au coût ou supplément commercial intégré au prix et à la rentabilité.
- Cotisations vente / services BIC / services BNC, répartition du CA, contributions et commissions paramétrables et désactivables ; aucun taux réglementaire prérempli.
- Prix plancher, conseillé et commercial ; remise et prix manuel recalculent charges, résultat estimé, marge réelle et résultats unitaires. Avertissement sous le seuil de rentabilité.
- Hypothèses sauvegardées dans le devis, calculs monétaires en decimal sans arrondis intermédiaires ; tests unitaires, de compatibilité et d’interface sans ouverture d’une application réelle.

### Estimations : quantité, forfait par pièce et amortissement du parc
- **Quantité de pièces** : le poids et la durée machine se saisissent pour une pièce (multipliés par la quantité) ou pour l'ensemble des pièces.
- **Forfait par pièce** (5 € par défaut) : ajouté au prix après la marge, quantité × forfait, avant la remise. Il n'est soumis ni aux aléas ni à la marge. Le récapitulatif l'affiche sur sa propre ligne.
- **Plus de choix d'imprimante** : le taux machine est l'amortissement horaire de toutes les imprimantes actives additionnées, et l'électricité utilise la puissance moyenne du parc.
- **Calculateur réinitialisé** après l'enregistrement et à l'ouverture depuis « Nouvelle estimation » ; « Réinitialiser » remet aussi projet et filament à zéro.
- L'électricité saisie à la main suit le même mode (par pièce ou ensemble) que le poids et la durée.

## 21 octobre 2026

### Projets terminés archivés, fiches historiques et finitions
- **Un projet passé à « Terminé » part à l'archivage** : son dossier est déplacé dans le `99_ARCHIVES` de son client, avec sa fiche. Le repasser à « À faire » ou « En cours » le ramène. Il reste visible dans la liste, et si le déplacement échoue le statut est quand même enregistré, avec un message.
- **Fiche client historique** : modifier la fiche ou changer le statut d'un projet dont le client n'a pas de fiche ouvre maintenant la complétion de la fiche client, au lieu d'un message sans issue.
- Un même message d'erreur n'est plus affiché deux fois dans les notifications.
- **Autocomplétion de la fiche client** : les listes d'entreprises et d'adresses ne s'ouvrent plus d'elles-mêmes à l'ouverture de la fiche ni après le choix d'une suggestion.
- **Liste des projets** : les références occupent une colonne fixe, les titres sont parfaitement alignés.
- Colonne STATUT alignée sur son en-tête et en-tête ACTIONS centré au-dessus des boutons (listes des projets et des clients).
- Toutes les listes : les pastilles (fiche, type) gardent leur largeur au lieu de remplir la colonne, l'en-tête ACTIONS est centré dans les listes compactes et l'en-tête de la corbeille compte les bonnes colonnes.
- **En-têtes de listes uniformisés** : même taille, même couleur et mêmes marges dans les listes triables et dans les listes compactes.
- **Filtre « Archivés »** dans la liste des projets : il n'affiche que les projets rangés dans `99_ARCHIVES`. Les autres filtres, « Tous » compris, ne listent que les projets actifs, et le compteur indique le nombre d'archivés.

## 16 octobre 2026

### Visualiseur 3D type découpeur : plateau et réorientation
- La pièce est **posée sur un plateau d'impression** : un rectangle plus clair avec contour dans la grille infinie, aux dimensions d'une imprimante de l'atelier (champ « volume d'impression » de la page Imprimantes), d'un plateau générique de 256 × 256 × 256 mm ou saisies à la main. Le contour passe en rouge et l'onglet Infos dit de combien la pièce dépasse ; la faisabilité signale « dépasse le volume d'impression ». La grille et le plateau sont affichés par défaut.
- **Réorientation** (onglet Outils) : rotations de ±90° autour de X, Y et Z, champs d'angles en degrés, retour à l'orientation d'origine, et **« Poser sur une face »** (touche F) : on clique la face qui doit devenir le dessous.
- **Orientation automatique** (touche O) : le logiciel essaie les six axes et les plus grandes faces planes, les évalue avec le moteur d'estimation et applique celle qui demande le moins de supports (ou la plus rapide, ou la plus stable) ; il indique le gain en supports et en durée. Sur le modèle de test du crâne (207 000 triangles), la recherche prend environ 1,6 seconde.
- Le poids, la durée, les supports, la faisabilité et la **découpe Bambu Studio** suivent l'orientation choisie. Le fichier d'origine n'est jamais modifié ; « Enregistrer le modèle orienté » écrit une copie STL en millimètres.
- Tourner la pièce efface les mesures, le plan de coupe et la comparaison, qui appartenaient à l'ancienne position.

### Visualiseur 3D : estimation instantanée et contrôle de faisabilité
- **Poids, durée et filament estimés tout de suite**, sans attendre le découpeur et même sans Bambu Studio installé : un moteur d'estimation maison découpe le modèle en couches et calcule les parois, le remplissage, les dessus et dessous. Il n'est calé sur aucune imprimante en particulier mais sur la **moyenne de cinq imprimantes** Bambu Lab (450 découpes de comparaison). Le poids est juste à 3 % près. Quand le découpeur a répondu, ses valeurs exactes remplacent l'estimation.
- La **durée** dépend de la rapidité réelle de la machine : la moyenne peut s'écarter d'environ 25 %. Le réglage **« Vitesse de la machine »** (moyenne, rapide, lente) ramène l'écart à environ 6 %. Le **débit maximal du matériau** (PLA, PETG, ABS, ASA, TPU, PA, PC, PVA) est pris en compte : le PETG et surtout le TPU sont plus lents que le PLA.
- Nouvelle case **« Avec supports »**, cochée par défaut : elle compte la matière des supports dans l'estimation et est transmise à Bambu Studio. Les pièces sans surplomb n'en reçoivent pas.
- Nouvelle section **Faisabilité** dans l'onglet Analyse, calculée dès l'ouverture du modèle : maillage non étanche, arêtes partagées par trop de faces, faces retournées, pièces séparées, parois trop fines pour la buse, surplombs (aire et matière de supports), base posée trop petite, unités probablement fausses. Chaque point a une gravité (erreur, avertissement, information) et un bouton **« Voir sur le modèle »** qui surligne les zones concernées.
- Le verdict (« Imprimable », « Imprimable avec réserves », « À corriger avant impression ») s'affiche aussi dans l'onglet Infos.
- « Créer une estimation avec ce modèle » reprend la durée estimée quand le découpeur n'a pas répondu.
- **Grille au sol infinie** : elle ne s'arrête plus à quelques longueurs de la pièce. Elle s'étend jusqu'à l'horizon à n'importe quel zoom, avec des lignes plus espacées qui prennent le relais quand on s'éloigne, et se fond dans le fond à l'horizon.
- **Zoom sans limite** dans le visualiseur : la molette zoome vers le **curseur** (le point visé reste sous le curseur) sans butée, de très loin jusqu'à l'intérieur de la pièce. Les plans de coupe de la caméra suivent la distance : le modèle n'est plus tronqué de près et ne scintille plus de loin. En mode « Logiciel (processeur) », les triangles passant derrière la caméra restent ignorés à très fort zoom.
- Limites : les supports sont sous-estimés sur les pièces organiques, une pièce flottante n'est pas détectée, et ce contrôle géométrique ne remplace pas un essai dans le découpeur. Détails et mesures dans `docs/estimateur-banc-de-comparaison.md`.

## 14 octobre 2026 (suite)

### Téléchargements : ZIP ouverts en silence et fenêtre centrée
- Un fichier **ZIP** téléchargé depuis la recherche de modèles est ouvert en arrière-plan : seuls les fichiers 3D (STL, 3MF, OBJ, STEP, AMF) sont gardés, le ZIP est supprimé. Les archives piégées sont neutralisées (aucun fichier n'est écrit hors du dossier du tampon, tailles et nombre de fichiers limités) ; une archive sans modèle ou protégée par mot de passe affiche un message clair.
- La bande de téléchargement est remplacée par une **fenêtre centrée sur le logiciel** : liste des fichiers avec case à cocher, rappel de licence, « Ouvrir dans le slicer », « Autre application… » et « Afficher le dossier ».
- **Jamais plusieurs fenêtres ni plusieurs slicers** : les fichiers qui arrivent pendant que la fenêtre est ouverte s'ajoutent à sa liste, et « Ouvrir » envoie tous les fichiers cochés dans **un seul** lancement du slicer ; le bouton reste grisé 3 secondes pour éviter un second clic.
- Les dossiers de ZIP ouverts sont supprimés du tampon après 7 jours, comme les fichiers.
- Le panneau « Téléchargements » du moteur web n'est plus désactivé : cet essai faisait apparaître un carré rouge sur la page.
- Le chargement à la suite va jusqu'à **8 pages** au lieu de 5 (huit sites cochés se chargent tous, un à un) ; au-delà, ou pour Yeggi, l'onglet se charge au clic. Plus de pages chargées, c'est plus de mémoire : environ 450 à 620 Mo par page.

### Page de démarrage des modèles 3D plus aérée
- Le choix des sites n'est plus enfermé dans une carte : il se pose directement sur la page, sous la recherche, qui reste le seul élément en relief.
- Le titre du choix affiche le nombre de sites cochés (« 3 sur 15 »), et les sites cochés portent une coche, plus seulement une couleur.
- Le bouton de recherche devient un vrai bouton d'action, la phrase d'aide est raccourcie, les espacements suivent le contenu (serrés dans un groupe, larges entre les groupes) et les raccourcis clavier s'affichent comme des touches.
- Deuxième passe sur cette page : titre plus grand avec sa phrase d'aide juste dessous, champ de recherche plus haut aux coins plus doux, un léger halo de la couleur d'accent derrière la recherche, et les quatre groupes de sites rangés deux par deux (le nom du groupe au-dessus de ses sites) au lieu d'une longue colonne. « Tout sélectionner » prend la couleur d'accent.
- Un clic sur un nom de site ne fait plus bouger la page : la place de la coche est toujours réservée (un « + » discret sur les sites décochés, une coche sur les sites cochés), donc les pastilles gardent leur largeur et les lignes ne se réorganisent plus.

### Même barre d'outils compacte sur tout le logiciel
- **Clients** et **Collections** reprennent la barre d'outils unique des Projets (filtre ou pastilles, nombre, actualiser, créer sur une seule ligne).
- **Filaments, Imprimantes, Collections, Corbeille, Estimations, Clients, Projets** : « Actualiser » devient un bouton-icône placé avant l'action principale (« Créer », « Ajouter ») ; la deuxième phrase de présentation, qui répétait celle du titre, disparaît.
- Le champ de recherche globale ne coupe plus son texte (« Projet, client, fichier… »).
- Sur l'accueil, les deux cartes prennent la hauteur de leur contenu au lieu de s'étirer jusqu'en bas de la fenêtre.
- Le sous-titre du visualiseur 3D cite aussi les fichiers 3MF.

### Page Projets plus dense et plus utile
- Une seule barre d'outils au lieu de deux lignes : filtre, pastilles de statut, nombre de projets, actualiser (icône) et « Créer » tiennent sur la même ligne, ce qui donne plus de place à la liste.
- Nouvelle colonne **Échéance**, triable. Une échéance dépassée sur un projet non terminé s'affiche « En retard · jj/mm » en couleur d'alerte.
- La référence du projet est écrite petite et discrète devant son nom, qui devient le texte qui ressort.

### Creality Cloud
- La recherche ouvre maintenant la page des modèles (`/fr/search/models?q=…`) : l'ancienne adresse affichait une page de résultats vide, sans aucun modèle.

### Page Paramètres remise en ordre
- Fini la phrase répétée sous le titre et le bouton « Actualiser » seul sur sa ligne : « Actualiser » est maintenant dans la carte « Stockage ».
- Le dossier de l'espace de travail s'affiche dans un champ dont on peut sélectionner le texte (pour le copier), les trois actions portent une icône, et quand l'espace est imposé par la variable `MS3DPRINT_WORKSPACE`, la raison est écrite sous les boutons au lieu d'être cachée dans une infobulle.
- Chaque lieu de stockage disponible indique son dossier, et la carte du bas affiche la **version et la révision** du logiciel installé (utile pour savoir quelle livraison tourne). Les cartes ne s'étirent plus sur toute la largeur d'une grande fenêtre.

### Fenêtre de téléchargement plus claire
- Le titre suit le contenu (« Modèle 3D reçu », « 3 modèles 3D reçus »), avec le nombre de fichiers et le poids total juste dessous.
- Chaque fichier est une ligne propre : icône, nom sans l'extension, pastille du format (STL, 3MF…) et taille ; les cases à cocher n'apparaissent que s'il y a plusieurs fichiers à choisir.
- Le rappel de licence devient une note à part, et les actions secondaires (« Ranger dans un projet », « Autre application », « Afficher le dossier ») passent sous la liste avec une icône ; le pied de fenêtre ne garde que « Fermer » et l'action principale.

### Barre d'adresse du navigateur
- Le champ d'adresse est « creusé » dans la barre (plus sombre qu'elle, coins plus doux, trait d'accent quand on y écrit) : il se lit comme un champ de saisie et non comme un bouton de plus.
- À gauche de l'adresse, un cadenas indique une page en https ; une page en http affiche un avertissement (« connexion non chiffrée ») ; la loupe revient dès qu'on écrit dans le champ.

### Barre d'onglets du navigateur
- L'onglet affiché porte un fin trait d'accent en haut et reste soudé à la barre d'adresse ; la croix de fermeture n'apparaît que sur l'onglet affiché et sous le pointeur (fini les huit croix alignées).
- Une infobulle sur chaque onglet donne son nom, le site et, pour un onglet en veille, « la page se charge au clic ». L'onglet choisi est toujours ramené dans la zone visible quand il y a beaucoup d'onglets.

### Tampon de téléchargement sous contrôle
- Le tampon garde **2 jours** (au lieu de 7) et **1 Go au plus** : quand il dépasse, les fichiers et dossiers de ZIP les plus anciens sont supprimés en premier. Les téléchargements en cours et les fichiers affichés dans la fenêtre de téléchargement ne sont jamais supprimés.
- Le nettoyage se fait à chaque téléchargement terminé, au chargement de la page et à la fermeture du logiciel.
- Nouveau bouton **« Ranger dans un projet… »** dans la fenêtre de téléchargement : les fichiers cochés sont déplacés du tampon vers un dossier de l'espace de travail (sélecteur intégré), sans jamais remplacer un fichier existant ; ils ne sont plus jamais nettoyés.

### Fichiers 3MF, déplacements et aperçu agrandi
- Les fichiers **3MF** (Bambu Studio, OrcaSlicer, PrusaSlicer, CAD) s'ouvrent maintenant dans le visualiseur 3D et ont une vignette dans les listes : tous les objets du plateau, avec leur position, les assemblages de composants et les unités du fichier ; les supports et objets non imprimables sont ignorés.
- « Déplacer » (fichier, dossier, client, collection) ne lance plus l'Explorateur Windows : un sélecteur de dossier intégré, limité à l'espace de travail, permet de parcourir et de choisir la destination.
- Quand le pointeur reste sur un fichier 3D dans une liste, un **aperçu agrandi** (360 px) s'affiche à côté ; il est calculé à la première visite puis gardé sur le disque.

## 14 octobre 2026

### Ouvrir un fichier sans application associée
- « Ouvrir avec… » affiche maintenant la vraie fenêtre « Ouvrir avec » de Windows, même quand le type de fichier n'a pas d'application (l'ancienne commande échouait avec « Aucune application n'est associée au fichier spécifié »). Cela arrivait pour les `.3mf` quand le choix par défaut de Windows pointait vers une application du Microsoft Store supprimée.
- Un double clic ou « Ouvrir dans le slicer » sur un modèle 3D sans application associée l'envoie directement à Bambu Studio ou OrcaSlicer s'ils sont installés, sinon affiche la fenêtre « Ouvrir avec ». Les erreurs affichent maintenant la cause au lieu d'un message générique.

### Corrections de la recherche de modèles 3D
- Plus de notification « Une erreur inattendue s'est produite (0x8007139F) » : le logiciel lit l'adresse de la page affichée toutes les 1,2 seconde, et le moteur web répond « état incorrect » pendant qu'une page démarre ou se ferme. Cette réponse est maintenant ignorée (elle ne servait qu'à rafraîchir la barre d'adresse), et la lecture n'est tentée que sur une page chargée. Les boutons précédent, suivant et actualiser et l'ouverture d'adresse sont protégés de la même façon.
- Un clic dans le champ de recherche de la page de démarrage arrête l'animation et affiche le texte fixe « Rechercher un modèle 3D ou saisir une adresse » ; elle reprend si vous quittez le champ sans rien saisir.

### Onglets chargés l'un après l'autre
- Après une recherche, le premier onglet s'affiche et les autres se chargent en arrière-plan, **un à la fois** (chacun démarre quand le précédent a fini, ou après 25 secondes), pour qu'un clic sur un onglet montre une page déjà prête. Un point bleu sur l'onglet indique qu'il charge ; un nom grisé, qu'il n'est pas encore chargé.
- Jusqu'à 5 pages restent chargées ; au-delà, la moins récemment vue se met en veille. Un onglet non regardé pendant 10 minutes se met en veille.
- **Yeggi n'est jamais préchargé** : sa page, très chargée en publicités, ouvre une trentaine de processus et pèse environ 2 Go à elle seule (Printables, MakerWorld et Thangs : 450 à 620 Mo chacun, moteur compris). Il ne se charge qu'à l'ouverture de son onglet. Thangs passe en tête des moteurs, et les sites cochés par défaut deviennent Thangs, Printables et MakerWorld.
### Page de démarrage des modèles 3D
- Les exemples animés sont tous en français et parlent d'impression 3D : « spatule mastic », « support de bobine de filament », « boîte de rangement vis », « organiseur de câbles », « pièce de rechange électroménager », « poignée de tiroir », « figurine articulée », « vase spirale », « porte-clés personnalisé », « support de casque audio ».
- Avant la première recherche, la page est centrée au milieu de la fenêtre, avec un grand champ de recherche intégré (la barre du haut n'apparaît qu'une fois des onglets ouverts). Une flèche à droite du champ lance la recherche, comme Entrée.
- Des exemples (« spatule mastic », « support casque audio »…) se tapent lettre par lettre dans le champ, puis s'effacent. L'animation s'arrête dès que vous saisissez quelque chose, ou quand la page n'est plus affichée, et reste fixe si Windows demande moins d'animations.

### Nozzlehub retiré, tampon pour les téléchargements
- Nozzlehub est retiré de la liste des sites (15 sites restent) ; Yeggi, Printables et MakerWorld sont cochés par défaut. Anycubic (Makeronline) n'a pas d'adresse de recherche directe et n'est donc plus proposé par ce biais.
- Les fichiers téléchargés dans le navigateur intégré ne vont plus dans vos Téléchargements : ils arrivent dans un tampon du logiciel (`%LOCALAPPDATA%\MS3DPRINT Manager\tampon`, vidé après 7 jours). Dès qu'un fichier STL, 3MF, OBJ, STEP ou AMF est complet, une bande « Fichier 3D reçu » propose **Ouvrir dans le slicer** (l'application que Windows associe au type de fichier), **Autre application…** et **Afficher le dossier**. Rien ne s'ouvre sans votre clic ; programmes et scripts restent bloqués.
- Le haut de la page devient une seule barre façon navigateur : un seul champ pour un mot-clé (recherche sur les sites cochés) ou une adresse (ouverte directement). Le choix des sites est sur la page de démarrage, puis dans le bouton « Sites ».
### Recherche de modèles 3D : mémoire et rapidité
- Les pages ne se chargent plus toutes en même temps : un onglet n'est créé que lorsqu'on l'affiche (une recherche sur 16 sites n'en charge qu'un). Mesure : 1,3 Go de mémoire pour le navigateur intégré avant, environ 1 Go après, avec 16 sites cochés.
- Trois pages au plus restent chargées ; les autres onglets passent « en veille » (nom grisé) et se rechargent à leur adresse quand on les ouvre.
- Un onglet non regardé depuis 3 minutes passe en veille, et toutes les pages se libèrent après 5 minutes passées sur une autre page du logiciel : la mémoire revient alors à celle du logiciel seul.

### Recherche de modèles 3D : plus de place pour le navigateur
- Le haut de la page tient en une ligne : champ de recherche, bouton « Sites · 3/16 » et « Rechercher ». Les sites se choisissent par pastilles groupées par catégorie dans un panneau qui se replie après la recherche (le bouton « Sites » le rouvre), avec « Tout sélectionner / Tout désélectionner ».
- Une recherche affiche d'abord l'onglet du premier site de la liste (au lieu du dernier ouvert).
- Barre d'onglets et barre de navigation façon navigateur : « + » juste après le dernier onglet, fermeture par la croix ou le clic du milieu, précédent et suivant grisés quand ils ne servent pas, barre de chargement, bouton « Ouvrir dans le navigateur de Windows » (utile pour une connexion ou une vérification « humain »).
- L'adresse affichée suit la page même quand le site change de page sans la recharger, et le titre de la page s'affiche en infobulle sur l'onglet.
- Raccourcis : `Ctrl+L` (adresse), `Ctrl+T` (nouvel onglet), `Ctrl+W` (fermer l'onglet), `F5` (actualiser).
- La page gagne de la hauteur et de la largeur : marges réduites et sous-titre masqué ; un état vide explique le fonctionnement.
### Recherche de modèles 3D : communautés des fabricants
- Quatre sites s'ajoutent : **Nozzlehub** (moteur qui réunit Printables, MakerWorld, Creality Cloud, Thingiverse, Cults3D, MyMiniFactory, Thangs, Nexprint, Snapmaker Space et Makeronline, avec un filtre sur les licences qui autorisent la vente), et les plateformes de fabricants **Creality Cloud**, **Nexprint** (Elegoo) et **Snapmaker Space**. MakerWorld (Bambu Lab) rejoint ce nouveau groupe « Fabricants d'imprimantes » : la liste compte 16 sites en 4 catégories.
- Anycubic (Makeronline) n'a pas d'adresse de recherche directe : ses modèles se trouvent via Nozzlehub. Aucune plateforme de modèles n'a été trouvée pour Flashforge, Qidi, Sovol ni Artillery.
- Nozzlehub est coché par défaut avec Printables et MakerWorld.
- Relancer une recherche réutilise l'onglet existant de chaque site au lieu d'en ouvrir un second.

### Recherche de modèles 3D : douze sites, par catégories
- Cinq sites s'ajoutent à la recherche : Sketchfab (modèles téléchargeables), GrabCAD, TraceParts, Smithsonian 3D et CGTrader. Les douze sites sont rangés par catégories (moteurs de recherche, communautés, besoins précis) avec une infobulle qui décrit chacun.
- Un bouton « Tout sélectionner / Tout désélectionner » coche ou décoche tous les sites d'un coup ; il propose l'action inverse une fois tous les sites cochés.
- Un rappel invite à vérifier la licence de chaque modèle avant de l'imprimer pour un client.
- Le site Smithsonian 3D peut demander une vérification « je suis humain » au premier accès ; elle reste à votre charge.

### Recherche de modèles 3D sur plusieurs sites
- Nouvelle page « Modèles 3D » (menu Outils) : une saisie, des cases à cocher pour Yeggi, MakerWorld, Printables, Thingiverse, Thangs, Cults3D et MyMiniFactory, et un onglet de navigateur intégré s'ouvre par site coché sur les résultats.
- Barre d'adresse (une adresse ou un texte à rechercher), précédent, suivant, actualiser, nouvel onglet, fermeture d'onglet ; un lien qui demande une nouvelle fenêtre s'ouvre dans un onglet.
- Le navigateur garde ses cookies et son cache dans `%LOCALAPPDATA%\MS3DPRINT Manager\webview` et non à côté de l'exécutable.
- Le logiciel déclare maintenant les versions de Windows qu'il prend en charge (manifeste), ce que le navigateur intégré exige pour s'afficher.
- La page occupe toute la largeur de la fenêtre, comme un navigateur (comme le visualiseur 3D), au lieu de la largeur de lecture des autres pages.
- `Ctrl+0` ouvre la dixième page (Paramètres), `Ctrl+9` la neuvième.

## 13 octobre 2026

### Audit : robustesse
- Le registre des codes clients ne garde que les cinq dernières sauvegardes archivées et n'attend plus indéfiniment s'il est verrouillé : au bout de 30 secondes, un message demande de réessayer.
- Le visualiseur 3D refuse avec un message clair un fichier de plus de 400 Mo, qui épuiserait la mémoire.
- Le journal `erreurs.log` est mis de côté (`erreurs.old.log`) au-delà de 1 Mo.
- Le découpeur automatique est arrêté s'il ne répond pas au bout de 10 minutes.
- Une estimation refuse un coût négatif ; le champ « Marge » devient « Marge sur coût », ce qu'il calcule réellement (prix = coût avec aléas × (1 + marge)), et le message d'erreur couvre le taux d'échec, la remise et la marge.

### Démarrage plus rapide
- L'écran de démarrage ne dure plus 5 secondes fixes : il se ferme dès que l'espace de travail est lu, au bout d'une seconde environ au minimum.

### Visualiseur 3D : grille au sol
- Nouvelle option « Grille au sol » (onglet Affichage, touche `G`) : une grille posée sous le modèle donne un repère de sol et d'échelle ; elle s'estompe dans le lointain et passe derrière le modèle.
- Les carreaux ont une taille ronde (1, 2, 5, 10, 20… mm) choisie pour en avoir une dizaine sur la plus grande dimension, et suivent l'unité du modèle ; leur taille est indiquée sous la case.
- Disponible avec le rendu par carte graphique ; elle apparaît aussi sur l'image enregistrée de la vue.

### Visualiseur 3D : cadre de focus
- Le cadre bleu autour de la vue 3D n’apparaît plus après un clic ni à l’ouverture d’un modèle : il ne s’affiche que lorsque la vue reçoit le focus au clavier (Tab).

### Espace de travail configurable
- Nouveau bouton « Changer d’espace de travail… » dans Paramètres : le dossier choisi est mémorisé (`data/workspace.json`) et remplace l’emplacement fixe d’origine, qui reste la valeur par défaut. Un dossier disparu ou un fichier illisible ramène à l’emplacement par défaut.

### Audit : sécurité et fiches illisibles
- Un programme ou un script (`.exe`, `.bat`, `.ps1`, `.lnk`, `.vbs`…) trouvé dans un dossier de projet n'est plus lancé par un double clic ni par « Ouvrir avec… » : le logiciel l'explique et invite à ouvrir son dossier dans l'Explorateur.
- Une fiche d'estimation, de filament ou d'imprimante illisible n'empêche plus d'afficher les autres ni de changer le code d'un client.
- L'enregistrement des estimations est protégé contre deux modifications simultanées, comme les autres fiches.

## 12 octobre 2026

### Audit : accessibilité et entretien
- Les onglets du visualiseur indiquent à un lecteur d’écran lequel est sélectionné ; la vue 3D est entourée de la couleur d’accent quand elle a le clavier, ce qui montre que les raccourcis (R, W, M, C, P) sont actifs.
- Les vignettes inutilisées depuis trois mois sont supprimées du disque au lancement.

### Image de la vue 3D et vignettes
- Nouveau bouton « Enregistrer une image » (touche `P`) dans la barre d’outils du visualiseur : la vue, telle qu’elle est à l’écran (coupe, comparaison, couleur), est enregistrée en PNG dans le dossier `06_PHOTOS_RENDUS` du projet du modèle ; hors d’un projet, le logiciel demande où l’enregistrer.
- Les fichiers STL et OBJ des projets et des dossiers s’affichent avec une vignette du modèle au lieu d’une icône ; les vignettes sont calculées en arrière-plan, gardées sur le disque jusqu’à ce que le fichier change, et ignorées au-delà de 40 Mo.

### Changer le code d’un client
- Nouveau bouton « Changer le code » dans la fiche client et dans le menu de sa ligne : il propose un code (initiales du nom, ex. FA pour Fell Agency) et montre, à mesure que vous tapez, les dossiers de projet qui seront renommés (F-2026-001_SAMURAI_70CM devient FA-2026-001_SAMURAI_70CM).
- Au changement, les dossiers, les fiches projet, les références réservées et les estimations liées suivent ; en cas d’erreur tout est remis en l’état. Rien n’est modifié avant la confirmation.

### Visualiseur 3D : rotation et découpage
- La rotation à la souris est inversée sur l’horizontale : faire glisser vers la droite tourne le modèle vers la droite.
- Les imprimantes à deux buses (H2D) ne peuvent pas être découpées en ligne de commande (erreur « multi-extruder ») : le logiciel découpe une H2D comme une H2S, à une seule buse, et l’indique ; un message clair remplace l’erreur du découpeur.

### Visualiseur 3D : nouvelle interface
- La vue 3D reste au centre ; une barre d’outils flottante en haut à gauche regroupe recentrer, filaire, mesurer, couper et comparer, chacun avec son icône et son raccourci.
- Le panneau de droite passe de neuf sections empilées à quatre onglets : Infos (modèle, unités, résultats, matière, durée d’impression), Affichage, Outils (réglages de l’outil actif) et Analyse.
- Les résultats sont présentés en lignes alignées, durée, poids et coût d’abord, puis volume, surface et filament ; le bouton « Créer une estimation avec ce modèle » reste toujours visible en bas du panneau.
- Activer un outil ouvre son onglet ; les réglages d’un outil inactif ne s’affichent plus.

### Accents des noms de projets et de clients
- Les noms de dossier perdent leurs accents (« EPEE_FOURREAU_REPLIQUE ») : l’affichage les retrouve pour les mots usuels (« Épée Fourreau Réplique », « Pions d’Échecs », « MV Créations », « Gâchette de Tronçonneuse »). Les dossiers sur le disque ne sont pas renommés.
- Un projet créé depuis le logiciel garde désormais le nom tel que saisi (accents, espaces, majuscules) dans sa fiche, au lieu de la version normalisée du dossier ; la fiche est créée dès la création pour un client qui a une fiche.
- « Modifier la fiche » propose le nom du projet lisible et permet de le corriger ; « Compléter la fiche » d’un projet historique propose le nom avec ses accents.

### Position de la fenêtre mémorisée
- La fenêtre se rouvre à l’endroit où vous l’avez laissée, sur le bon écran, avec sa taille ; fermée en plein écran, elle se rouvre en plein écran et revient à sa position d’avant en la réduisant.
- Si cet endroit n’est plus visible (écran débranché, résolution changée), elle est recentrée sur l’écran principal.

### Comparaison de deux versions
- « Comparer avec une autre version » charge un second fichier et le superpose au premier dans une autre couleur (rouge sur bleu) : les zones rouges montrent où la version B dépasse A, les bleues où A dépasse B.
- Tableau des écarts en mm : largeur, profondeur, hauteur, volume, surface et nombre de triangles ; affichage de A ou de B seul, alignement des centres.
- La comparaison utilise le rendu par carte graphique (le logiciel y repasse automatiquement).

### Plan de coupe
- Nouvelle section « Plan de coupe » (touche `C`) : une coupe horizontale (hauteur) ou verticale selon X ou Y, réglée au curseur, avec le choix du côté conservé et la position affichée en mm.
- La face coupée apparaît en orange pour lire les parois et les cavités ; mesures et sélection de points ignorent la partie coupée.
- Les boutons à bascule, curseurs et cases à cocher reprennent la couleur d’accent de l’application.

### Unités du modèle mémorisées
- Un seul choix d’unité (mm, cm, m, pouces ou personnalisée) remplace l’échelle à saisir et la case de confirmation ; il est mémorisé pour chaque fichier.
- Les dimensions s’affichent dans l’unité choisie et en mm, une alerte signale un modèle de taille improbable, et mesures, volume, poids, analyse des parois et découpage suivent l’unité.
- Le découpeur reçoit l’échelle : un modèle dessiné en pouces est découpé à la bonne taille.

### Découpage automatique (Bambu Studio / OrcaSlicer)
- Le logiciel détecte Bambu Studio ou OrcaSlicer et découpe le modèle ouvert dans le visualiseur sans afficher sa fenêtre : durée d’impression, longueur de filament et poids réels sont affichés au chargement.
- L’imprimante (profil machine) est choisie parmi vos imprimantes du logiciel, la qualité parmi les profils compatibles, et le filament selon la matière ; le remplissage et les parois du panneau sont transmis au découpeur.
- « Créer une estimation avec ce modèle » reprend maintenant la durée machine, le poids exact, l’imprimante et le filament : on passe du fichier au prix en un clic.

### Du visualiseur à l’estimation
- Bouton « Créer une estimation avec ce modèle » : ouvre une nouvelle estimation préremplie avec le nom du fichier, le poids estimé, le filament choisi et le projet auquel appartient le fichier.
- Une nouvelle estimation reprend le taux horaire de l’imprimante sélectionnée au lieu d’une valeur par défaut.

### Visualiseur 3D : volume et poids
- Le panneau indique le volume et la surface du modèle, puis le poids estimé selon la matière (PLA, PETG, ABS, ASA, TPU, Nylon, PC, PVA ou l’un de vos filaments), le remplissage et l’épaisseur des parois.
- Avec un filament de votre catalogue, le coût de matière est calculé ; un maillage ouvert est signalé.
- Correction : la rotation, le déplacement, le zoom et les clics de mesure fonctionnent de nouveau avec le rendu par carte graphique.

### Visualiseur 3D : mesures
- Nouvel outil « Mesurer des distances » (touche `M`) : cliquez deux points du modèle, la longueur s’affiche sur la vue et dans le panneau avec ses écarts en X, Y et Z.
- Les points s’accrochent aux sommets proches ; plusieurs mesures peuvent être gardées, `Échap` annule le point en cours.
- Les longueurs sont exprimées en mm avec l’échelle (mm par unité du modèle) indiquée dans le panneau.

### Visualiseur 3D : finition
- Le menu « Visualiseur 3D » est mis en évidence quand il est ouvert ; bouton « Ouvrir un fichier » au centre de la vue vide.
- Pastilles de couleur avec anneau de sélection, chemin du fichier abrégé (infobulle complète), résultat de l’analyse des parois résumé en une ligne avec le détail dessous.

### Visualiseur 3D accéléré
- Rendu par la carte graphique (OpenGL via ANGLE) : le modèle reste sur le GPU, rotation fluide même avec plusieurs millions de triangles, bords lissés (multi-échantillonnage 4x), points d’analyse des parois affichés par le GPU.
- Choix du rendu (carte graphique ou processeur) dans le panneau ; bascule automatique vers le processeur si aucun contexte graphique n’est disponible.
- Le visualiseur occupe toute la largeur de la fenêtre.

### Visualiseur 3D intégré
- Le visualiseur 3D s’ouvre dans la fenêtre du logiciel (plus de fenêtre séparée), avec un bouton Retour qui ramène à la page précédente.
- Vue plus grande, panneau latéral (modèle, affichage, analyse), pastilles de couleur, raccourcis `R` (recentrer) et `W` (filaire), dépôt de fichier directement sur la vue.

### Dossiers modifiables
- Dans le contenu d’un dossier de collection : **Nouveau dossier**, **Importer un fichier**, et pour chaque ligne un menu « ⋯ » (renommer, déplacer, corbeille, ouvrir avec…).
- Dans un projet, les sous-dossiers ont aussi leur menu d’actions.
- Ouvrir un dossier remplace la page affichée au lieu de se superposer à elle.

### Audit accessibilité et robustesse
- Boutons à icône et lignes de listes nommés pour les lecteurs d’écran ; boutons d’action agrandis (36 px).
- Textes de 10 et 11 px portés à 12 px ; couleurs restantes passées en jetons de thème.
- La page Estimations et le choix de dossier d’import ne figent plus la fenêtre pendant la lecture du disque.
- Les animations sont coupées quand Windows demande moins d’animations ; l’étape terminée de l’estimation affiche une icône dessinée.
- Ajout de `PRODUCT.md` et `DESIGN.md` pour cadrer les prochaines évolutions.

### Passe Impeccable
- Vraies icônes dessinées (menus d’actions, boutons, tri des colonnes, notifications) à la place des symboles texte.
- Menu actif sans liseré coloré ; pages posées directement sur le fond, sans cartes imbriquées ni titres répétés.
- Accueil : trois indicateurs réunis dans un seul bandeau ; plus de défilement qui masque les boutons des listes.

### Passe de finition
- Fiche client, explorateur de dossiers et corbeille repris dans le même style de tableau (icônes, badges, dates).
- Une erreur inattendue ne ferme plus le logiciel : elle s’affiche en bas à droite et est consignée dans
  `%LOCALAPPDATA%\MS3DPRINT Manager\erreurs.log`.
- Textes complets en infobulle quand une cellule est tronquée, noms accessibles pour les boutons à icône.
- README réécrit pour décrire le logiciel actuel (navigation, clients, projets, estimations, aperçus, dépannage).

### Deuxième passe sur l’interface
- Fil d’Ariane cliquable (Projets › Projet › sous-dossier, ou Clients › Client › Projet) pour remonter en un clic.
- Colonnes triables en cliquant sur l’en-tête (Projets et Clients), clic droit sur une ligne pour ouvrir ses actions.
- Accueil : bloc « À surveiller » (échéances proches ou dépassées, fiches projet et client à compléter) avec accès direct aux listes filtrées.
- Menu : compteurs sur Projets et Clients ; en dessous de 1100 px il se réduit à une barre d’icônes.
- Recherche : résultats avec icône et type, navigation au clavier (↑ ↓ puis Entrée).
- Le logiciel retient la taille de sa fenêtre ; transitions douces sur les boutons et les pages.
- Fiche projet épurée : plus de titre en double, statut et échéance sur une ligne.

### Refonte de l’interface
- Nouveau système de design : couleurs plus contrastées, typographie, boutons (principal, secondaire, discret, danger),
  champs, badges de statut colorés et cartes cohérents dans tout le logiciel.
- Menu latéral avec icônes, repère de la page active et raccourcis clavier : Ctrl+1 à Ctrl+9 pour changer de page,
  Ctrl+N pour créer un projet, Ctrl+K pour rechercher.
- Tableau de bord : indicateurs avec icônes, cinq projets récents (statut en badge), actions rapides
  (nouveau projet, client, estimation), mis à jour à chaque retour sur l’accueil.
- Filtres de recherche et de statut dans Projets et Clients, avec le nombre de résultats.
- Fenêtres de saisie unifiées : titre, aide, champs étiquetés, boutons en pied de fenêtre ; Entrée valide, Échap annule.
- Notifications discrètes en bas à droite à la place des messages sous le titre, et barre de chargement pendant les lectures.
- États vides explicatifs avec un bouton d’action, fiche projet en tableau (icônes, type, taille), page Paramètres en cartes.

### Nouveautés
- Écran de chargement au démarrage, avec le logo, le nom du logiciel et une barre de progression.
- Les projets récents du tableau de bord sont cliquables et ouvrent directement le projet.
- Nouvelle barre de recherche (Ctrl+K), plus lisible.
- Collections : filtre par type (Modèles 3D, Produits, Fournisseurs) et colonne « Type » dans le tableau.
- Visualiseur 3D accessible depuis le menu latéral.
- Journal des modifications accessible depuis Paramètres.
- Les PDF s’ouvrent dans une fenêtre aux couleurs du logiciel (pages, zoom, ajustement à la largeur).
- Nouveau visualiseur 3D intégré (STL et OBJ) : rotation, zoom, déplacement, mode solide ou filaire,
  couleur au choix et analyse indicative des parois fines.

- Création de client : recherche d’entreprise sur Internet pendant la saisie du nom (SIRET et adresse repris
  automatiquement), suggestions d’adresse, code client généré automatiquement (initiales, puis AL2, AL3… en cas de doublon) et dossier au format `NOM_CLIENT`.
- Import de fichier depuis la racine d’un projet : choix du dossier de destination (proposé selon le type de fichier) ; depuis un dossier, le fichier y est importé directement.
- Import de fichier : proposition de renommage `nom__RÉFÉRENCE-PROJET.ext` (modifiable), ou conservation du nom d’origine.
- Projets et Clients : le menu d’actions « ⋯ » (ouvrir, modifier, renommer, déplacer, corbeille) est sur chaque ligne
  de la liste ; dans un projet ou un client déjà ouvert il ne reste que « Modifier la fiche ».
- Fichiers d’un projet : action « Ouvrir avec… » (choix de l’application Windows) dans le menu « ⋯ ».
- Aperçu PDF : bouton « Imprimer… » qui ouvre la boîte d’impression de Windows (imprimante, propriétés, exemplaires, pages) ; la page est centrée dans la fenêtre.
- Page Projets : bouton « Créer » ; colonnes des tableaux Projets et Clients alignées sur toutes les lignes.
- Fiche client : bouton « Nouveau projet » (le client est déjà choisi).
- Clients : le code client est attribué automatiquement, affiché en direct dans la fenêtre de création, puis dans une
  colonne « Code » de la liste ; ajout de l’e-mail et du portable (fiche, création, recherche).
- Noms lisibles : « GUY_LALLEMAND » s’affiche « Guy Lallemand », « MPO_FRANCE » « MPO France » (les dossiers ne changent pas).
- Filaments : plus de clic sur la ligne, la modification est dans le menu « ⋯ ».

### Améliorations
- Projets, Clients, Collections, Filaments et Imprimantes s’affichent en tableau compact.
- Les fenêtres de saisie s’ouvrent au centre du logiciel.
- Chaque champ des fenêtres Imprimante et Filament indique son unité (€, h, W, €/kg…).
- Imprimantes : le volume d’impression est grisé pour l’instant et l’option « Imprimante active » est retirée.

### Corrections
- Création d’un client ou d’un projet : plus d’erreur « accès refusé » sur le dossier temporaire (la synchronisation
  Nextcloud retenait brièvement le dossier). Le renommage est réessayé quelques secondes, et l’interface ne se fige plus.
- Création de projet depuis une fiche client : plus d’erreur « le thread appelant ne peut pas accéder à cet objet »
  (même défaut corrigé pour le renommage, les dossiers, les collections et les fiches projet/client).
- Depuis une fiche client, ouvrir un projet affiche bien le projet (la page ne restait plus sur le contact),
  et « Retour » ramène à la fiche client. Les boutons « Créer » et « Actualiser » de la liste des clients ne
  s’affichent plus dans une fiche.
- Le logiciel ne plantait plus au démarrage (onglet Estimations).
- La création d’un projet depuis le tableau de bord ne plante plus.

### Retiré
- L’ancien moteur d’interface WPF a été entièrement supprimé : plus aucune fenêtre de l’ancien logiciel ne s’ouvre.
- « Classer un fichier » dans Paramètres (le classement se fait depuis les projets).

## 7 – 11 octobre 2026

- Refonte complète de l’interface sous Avalonia et .NET 10.
- Gestion des clients, projets, collections, filaments, imprimantes et estimations.
- Corbeille réversible, aperçu des images, documents et modèles 3D.
