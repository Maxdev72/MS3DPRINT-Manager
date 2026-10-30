# MS3DPRINT Manager

MS3DPRINT Manager est une application Windows portable destinée à organiser
les dossiers de MS3DPRINT. Elle est développée avec .NET 10 et Avalonia. La version
publiée est autonome et tient dans un exécutable unique.

## Prérequis

Le SDK .NET 10 est nécessaire pour compiler et exécuter l'application depuis
les sources. L'exécutable publié pour Windows x64 n'a pas besoin d'un runtime
.NET installé.

## Commandes

```powershell
dotnet test MS3DPRINT.Manager.sln
dotnet run --project src/MS3DPRINT.Manager.Windows.App/MS3DPRINT.Manager.Windows.App.csproj
dotnet publish src/MS3DPRINT.Manager.Windows.App/MS3DPRINT.Manager.Windows.App.csproj -p:PublishProfile=PortableWinX64 -o outputs/MS3DPRINT-Manager
```

## Livraison

Après publication, le binaire interne `outputs\MS3DPRINT-Manager\MS3DPRINT.Manager.Windows.App.exe`
est autonome et fonctionne sur Windows x64 sans installation du runtime .NET.
Le nom destiné à l’utilisateur est toujours **MS3DPRINT Manager.exe**.
Copiez le binaire publié sous ce nom dans le dossier de livraison et remplacez
la version précédente. Gardez un seul exécutable dans ce dossier, sans suffixe
Material, numéro de version ou date. Les éventuelles sauvegardes restent dans
`outputs\backups`, séparées de la livraison.

Si l'application a déjà enregistré des codes clients, conservez également
`data\client-codes.json` dans le dossier livré. Ce fichier contient le
registre local nécessaire pour retrouver ces codes.

## Utilisation

**Tarifs par quantité.** Dans l’estimateur, cliquez sur **Tarifs par quantité** à côté des ajustements du prix. Les paliers 1/5/10/20/50 pièces ont par défaut les remises 0/5/10/13/25 %. Modifiez les pourcentages pour voir les prix unitaires, totaux et marges estimées, puis **Conserver les remises** pour les retenir dans le devis et son brouillon. Enregistrez le devis pour les retrouver dans son historique. La grille part du prix commercial avant remise par pièce, ou du prix conseillé recalculé pour une pièce ; elle ne cumule pas la remise du devis. **Copier la grille** fournit les tarifs à communiquer au client. Les lots utilisent des projections de coûts : confirmez poids/durée dans le slicer ou prix du fournisseur avant de vous engager. Le prix et la quantité du devis restent ceux que vous avez saisis. Les anciens devis restent inchangés ; la fonction nécessite le moteur V2 et concerne les pièces imprimées.

**Charges et financements.** Dans **Achats → Échéancier**, ajoutez un financement ou une charge récurrente. Pour un prêt, saisissez les montants connus (dont une dernière mensualité différente), ou choisissez **Tableau bancaire** pour importer un CSV UTF-8 ou coller une sélection Excel tabulée. Colonnes obligatoires : Date et Montant/Mensualité ; facultatives : Capital, Intérêts, Assurance, Capital restant dû. Dates JJ/MM/AAAA ou AAAA-MM-JJ, montants au centime, échéances classées par date. Vérifiez l’aperçu avant d’enregistrer. Les PDF/images ne sont pas lus automatiquement. Dépliez les échéances puis confirmez chaque paiement à sa date réelle ; une date dépassée reste à payer. Les charges sans fin affichent une prévision sur une période visible. Le registre reste indépendant. Modifier conserve les confirmations ; annulez explicitement une confirmation avant de changer son échéance. Archiver conserve la fiche et ses paiements, consultables et restaurables dans Archives.

**Reclasser les tâches.** Dans Suivi, glissez la poignée à gauche d’une tâche vers le trait cyan avant ou après une autre tâche du même projet. L’ordre est enregistré automatiquement ; Échap annule. La poignée accepte aussi Alt + Haut/Bas, et le menu conserve Monter/Descendre.

**Suivi des projets.** Dans Projets, choisissez **Suivi**, puis dépliez un projet. Ajoutez une tâche dans le champ prévu (Entrée ou Ajouter), cochez-la quand elle est terminée et cliquez sur son texte pour modifier ses notes. Le menu « ⋯ » permet de monter, descendre ou retirer une tâche après confirmation. La première tâche restante devient la prochaine action. La recherche porte aussi sur les tâches et les notes ; les filtres et le tri par priorité restent disponibles. Les tâches sont sauvegardées dans la fiche du projet. Les brouillons d’ajout non validés sont conservés seulement pendant la session ; validez-les avant de fermer le logiciel.

**Prochaines actions sur l’accueil.** « À reprendre » affiche la prochaine tâche et le nombre de tâches terminées des projets de sa sélection. Cliquez sur un projet avec des tâches pour ouvrir directement son suivi. Revenir sur l’accueil actualise les informations. Les échéances et priorités existantes restent visibles ; terminer toutes les tâches ne clôture pas le projet.

**Calendrier des projets.** Dans Projets → Calendrier, consultez le mois et cliquez sur un jour pour retrouver ses échéances. « À venir » présente les dates dans l’ordre, y compris celles en retard. Dans « À planifier », choisissez un projet sans date puis Planifier ; la date sélectionnée est la même que dans la fiche et sur l’accueil. Modifier l’échéance ouvre également le sélecteur de date ; retirer une date demande confirmation. Les filtres restent accessibles par le bouton Filtres. Google Agenda n’est pas encore connecté.

**Factures et encaissements.** Dans **Recettes → Factures**, consultez les PDF et leur solde avec les filtres À encaisser, Partielles, Soldées et Toutes. Une facture sans paiement ne crée aucune recette. Cliquez sur **Enregistrer un paiement** quand le règlement arrive, indiquez sa date réelle et le montant reçu, même partiel. Le suivi tient compte des recettes existantes de toutes les années, de leurs corrections et annulations. **Importer plusieurs encaissements** permet toujours la saisie groupée des factures effectivement réglées.

**Navigation.** Le menu de gauche donne accès à l’Accueil, aux Projets, Clients, Collections, Filaments,
Estimations, Imprimantes, à la Corbeille, aux Modèles 3D (recherche) et aux Paramètres. `Ctrl+1` à `Ctrl+9`, puis `Ctrl+0`, changent de page, `Ctrl+N`
crée un projet et `Ctrl+K` ouvre la recherche. Sous 1100 px de large, le menu se réduit à une barre d’icônes.
Le logiciel retient la taille de sa fenêtre. Un fil d’Ariane cliquable indique où l’on se trouve dans un projet
ou une fiche client.

**Accueil.** Indicateurs (projets, devis, en cours), cinq projets récents, actions rapides et bloc « À surveiller »
(échéances proches ou dépassées, fiches à compléter).

**Projets.** La liste se filtre par texte et par statut, et se trie en cliquant sur un en-tête. Le menu « ⋯ » de
chaque ligne (ou un clic droit) permet d’ouvrir le dossier, modifier la fiche, renommer, déplacer vers un autre
client ou envoyer à la corbeille. Dans un projet : **Nouveau dossier**, **Importer un fichier** (à la racine du
projet, le dossier de destination se choisit ; dans un dossier, le fichier y est importé directement ; un nom
`nom__RÉFÉRENCE` est proposé), puis pour chaque fichier **Ouvrir avec…**, **Renommer**, **Déplacer** et
**Corbeille**. Rien n’est jamais écrasé à destination.

**Clients.** À la création, le **code client est attribué automatiquement** (initiales du nom, puis `AL2`, `AL3`…
en cas de doublon) et s’affiche en direct dans la fenêtre. On peut renseigner le SIRET, l’e-mail, le portable et
l’adresse. Pour un professionnel, la recherche d’entreprise (annuaire public des entreprises) et la recherche
d’adresse proposent des suggestions : la connexion Internet n’est nécessaire que pour elles. La liste affiche le
code, le type de fiche et le nombre de projets ; les clients historiques sans fiche gardent leur code lu dans les
références de leurs projets et se complètent depuis « Modifier la fiche ». Depuis une fiche client, **Nouveau
projet** crée un projet pour ce client.

**Collections, filaments, imprimantes.** Les collections regroupent modèles 3D, produits et fournisseurs (filtre
par type). Un filament a une marque, un nom, une matière, une couleur, un prix au kilo et un indicateur abrasif.
Une imprimante a un prix d’achat, une durée de vie et une puissance : son coût horaire alimente les estimations.

**Modèles 3D (recherche multi-sites).** Saisissez ce que vous cherchez, cochez les sites (Yeggi, MakerWorld,
Printables, Thingiverse, Thangs, Cults3D, MyMiniFactory) et « Rechercher » : un onglet s'ouvre par site, dans un
navigateur intégré, dont le premier de la liste s'affiche (précédent, suivant, actualiser, barre d'adresse,
nouvel onglet, fermeture, ouverture dans le navigateur de Windows ; `Ctrl+L`, `Ctrl+T`, `Ctrl+W`, `F5`). Le bouton
« Sites » replie ou déplie le choix des sites. Les liens qui s'ouvrent dans une nouvelle fenêtre ouvrent un onglet.
Les fichiers téléchargés par ce navigateur ne vont pas dans vos Téléchargements : ils arrivent dans un tampon du logiciel
(`%LOCALAPPDATA%\MS3DPRINT Manager	ampon`, vidé après 7 jours) et, pour un STL, 3MF, OBJ, STEP ou AMF complet, une bande
propose « Ouvrir dans le slicer » (l'application que Windows associe à ce type de fichier), « Autre application… » ou
« Afficher le dossier ». Rien ne s'ouvre sans clic. La page a besoin d'Internet et du composant Microsoft Edge
WebView2 (présent sous Windows 11, à installer au besoin sous Windows 10) ; les cookies et le cache des sites sont
gardés dans `%LOCALAPPDATA%\MS3DPRINT Manager\webview`, pas dans le dossier de livraison.

**Estimations.** Choisissez le type de prestation (impression 3D, modélisation seule, ou les deux), le filament
et l’imprimante. L’électricité se calcule en direct à partir de la durée machine, de la puissance de l’imprimante
et du prix du kWh. Calculez, enregistrez, rechargez ou envoyez à la corbeille une estimation.

La **V2** distingue commande, pièce conforme et tentative, gère les plateaux et les machines mobilisées,
les charges sur le chiffre d’affaires, la marge cible et les trois prix (plancher, conseillé, commercial).
Les anciens devis conservent leur moteur historique. Les taux de cotisations sont à renseigner selon votre
activité. Les profils France 2026 sont proposés dans **Paramètres → Atelier** ; les préférences restent propres à chaque espace de travail. Voir [le guide de l’estimateur V2](docs/estimateur-v2.md) et [l’explication de chaque champ](docs/guide-champs-estimateur.md).

**Corbeille.** Les suppressions sont réversibles : **Restaurer** remet l’élément à son emplacement d’origine (si
cet emplacement est occupé, libérez-le d’abord). Les références de projet restent réservées.

**Recherche (**`Ctrl+K`**).** Elle porte sur les noms, codes clients, références de projet, noms de fichiers, e-mails
et téléphones, à la demande et sans indexation. `↑` `↓` puis `Entrée` ouvrent un résultat.

**Aperçus.** Les images s’affichent dans le logiciel. Les PDF s’ouvrent dans une fenêtre intégrée (pages, zoom,
ajustement à la largeur) avec un bouton **Imprimer…** qui ouvre la boîte d’impression de Windows. Les fichiers
STL, OBJ et 3MF s’ouvrent dans le **visualiseur 3D** (rotation, zoom, déplacement, solide ou filaire, couleur,
dimensions en unités du modèle) ; son test de parois est facultatif et indicatif : il demande l’échelle en
millimètres et un seuil, signale des zones potentiellement fines et n’est pas une garantie d’imprimabilité.
Les autres formats s’ouvrent avec l’application Windows associée (**Ouvrir avec…** pour en choisir une).

**Paramètres.** Emplacement de l’espace de travail, vérification de l’arborescence, état des stockages et
journal des modifications.

**Dépannage.** Une erreur inattendue s’affiche en bas à droite sans fermer le logiciel et est consignée dans
`%LOCALAPPDATA%\MS3DPRINT Manager\erreurs.log`. L’aperçu PDF utilise le moteur intégré à Windows 10 (version
2004 ou plus récente) : aucun navigateur séparé n’est nécessaire. Le journal des modifications est dans
`CHANGELOG.md`.
