# Veille technologique MS3DPRINT — 25 octobre 2026

## Périmètre et méthode

Bilan ponctuel demandé par l’utilisateur : **logiciel MS3DPRINT Manager** et **impression 3D**, dans deux volets distincts. Sources officielles consultées le 25 octobre 2026, confrontées aux fichiers du dépôt et aux artefacts de la dernière publication. Les recommandations sont des analyses pour MS3DPRINT, pas des performances mesurées ni des achats à effectuer automatiquement.

Les versions stables sont distinguées des préversions. Les dates de publication d’Avalonia 12.1.4, OrcaSlicer 2.4.2 et Bambu Studio 2.8.2.61 ont aussi été vérifiées par l’API publique GitHub. Les annonces et fiches fabricants décrivent leurs produits ; elles ne constituent pas des essais indépendants. Ce bilan n’est ni un inventaire exhaustif de tous les fabricants ni une certification de sécurité. Aucun prix promotionnel n’est utilisé pour décider d’un achat.

Cette mission produit uniquement ce rapport. Elle ne change ni dépendances, ni code fonctionnel, ni données utilisateur, et ne crée pas de veille récurrente. L’amélioration des parcours fichiers, déjà acceptée, reste à réaliser. La facturation et le suivi de production mis de côté ne sont pas ajoutés au périmètre.

## Volet 1 — MS3DPRINT Manager

### État réel et versions à suivre

| Élément | Constat dans le projet | État externe vérifié | Décision proposée |
|---|---|---|---|
| .NET | Cible .NET 10 ; dernière configuration de publication Release win-x64 : runtime **10.0.13** | Correctif courant **10.0.13**, publié le 13 octobre 2026 ; branche LTS suivie jusqu’en novembre 2028 | Conserver .NET 10 et suivre les correctifs. Aucune urgence de migration majeure déduite de cette vérification |
| SDK | `global.json:3` demande **10.0.401**, avec `rollForward: latestFeature` | SDK **10.0.402** disponible | Vérifier le SDK effectivement sélectionné avant la prochaine publication ; le numéro de global.json n’est pas la version du runtime embarqué |
| Avalonia / Desktop / Fluent | Références **12.1.3**, `MS3DPRINT.Manager.Desktop.csproj:23` | Stable **12.1.4**, publiée le 9 octobre 2026 | Premier correctif technique à évaluer, avec tests de non-régression |
| WebView Avalonia | Référence **12.1.1**, même projet ligne 26 | Composant distinct : son cycle doit être vérifié séparément | Ne pas lui attribuer automatiquement le numéro du framework principal |
| System.Drawing.Common | Référence directe **10.0.0**, projet Windows ligne 10 ; utilisée par `PdfPrinter.cs` | Package Microsoft **10.0.13** disponible | Examiner les notes et compatibilités, puis mettre à jour séparément si approprié. Aucun CVE précis n’est déclaré par ce bilan |
| Livraison | Profil `PortableWinX64.pubxml:5` : autonome, fichier unique ; exécutable livré contrôlé avec `Get-AuthenticodeSignature` : **NotSigned** | Le runtime d’une application autonome est mis à jour par une nouvelle publication ; Windows propose des mécanismes de signature | Maintenir le fichier unique demandé ; étudier la signature pour la distribution à d’autres utilisateurs |

Sources : [support .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [SDK/runtime .NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), [Avalonia 12.1.4](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.4), [package System.Drawing.Common](https://www.nuget.org/packages/System.Drawing.Common/10.0.13), [publication autonome](https://learn.microsoft.com/en-us/dotnet/core/deploying/), [signature Windows](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control).

La configuration locale `src/MS3DPRINT.Manager.Windows.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/MS3DPRINT.Manager.Windows.App.runtimeconfig.json:7` et le fichier `.deps.json` associé déclarent 10.0.13. Cela caractérise les artefacts de publication lus, sans constituer une analyse de toutes les bibliothèques binaires ni un audit de vulnérabilités transitives. La signature de l’exécutable livré a été contrôlée directement.

### Correctifs Avalonia pertinents pour nos parcours

La 12.1.4 corrige notamment le focus restant sur un élément supprimé lors de la fermeture d’un popup, la position du curseur dans un texte centré, certains textes tronqués et la réattache de visuels. Elle améliore aussi la libération de ressources de texte et de rendu, ainsi que l’automatisation des menus. Ces sujets recoupent les menus, saisies et changements de pages déjà travaillés dans MS3DPRINT. Cela justifie un essai ciblé, pas la promesse d’un gain mesuré. [Notes de version](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.4).

**Validation proposée :** menus de ligne ouverts/fermés au clavier et à la souris ; caret des champs numériques ; focus rendu après panneau ; longues listes ; ouverture/fermeture répétée du visualiseur ; conservation des devis/brouillons et des sélections. Comparer mémoire et réactivité avant/après, en Release et avec les mêmes jeux de données.

### Fluidité et ergonomie : améliorer les zones qui en ont besoin

Les catalogues bénéficient déjà de `PagedRows` avec 50 lignes par page (`PagedRows.cs:15`). Les dossiers génériques et de projet créent actuellement une ligne pour chaque entrée (`MainWindow.Folders.cs:166,176`, `MainWindow.ProjectFiles.cs:95,103`). Les miniatures sont déjà calculées en arrière-plan et mises en cache ; les fichiers de plus de 40 Mio sont exclus de ce parcours (`ModelThumbnails.cs:19,55`).

**Analyse :** les dossiers très remplis sont un bon candidat à une limite de lignes ou une liste virtualisée. Une recherche locale et les filtres doivent réutiliser l’inventaire chargé, plutôt que relire le disque à chaque frappe. La documentation Avalonia confirme l’intérêt de la virtualisation, avec une hauteur de liste réellement contrainte ; la réduction de taille des images et les chargements asynchrones sont aussi recommandés. [Performance Avalonia](https://docs.avaloniaui.net/docs/app-development/performance), [guide des problèmes de performance](https://docs.avaloniaui.net/troubleshooting/app-performance-issues).

Le prochain lot fonctionnel cohérent est celui déjà accepté : **filtres par format, accès aux fichiers récents et aperçus plus lisibles**, dans les composants existants. Récents limités et propres à l’espace de travail ; fichier disparu signalé ; ouverture au clavier ; état sans résultat avec effacement du filtre. Les détails techniques restent secondaires, pas de nouveaux champs permanents dans l’estimateur.

Pour une campagne de mesure : temps d’arrivée des premières lignes, délai après saisie, mémoire après 20 changements de dossier et après 20 ouvertures de modèle. Jeux synthétiques de 100/1 000/10 000 fichiers, puis validation sur un échantillon métier autorisé. Ce sont des scénarios proposés : **aucune mesure de latence n’a été réalisée pendant cette veille**.

### Outils, dépendances et architecture

Avalonia a publié son Hot Reload officiel le 2 septembre 2026. Il peut accélérer les essais XAML/C#, mais relève de l’outillage payant ; certains contrôles reconstruits perdent leur état local. Le profiler et certaines fonctions DevTools relèvent également d’offres sous licence. Leur intérêt serait de réduire le temps de diagnostic, sans devenir une dépendance obligatoire de l’application livrée. [Hot Reload](https://avaloniaui.net/blog/hot-reload-ga), [éditions DevTools](https://avaloniaui.net/devtools).

Pour les dépendances, prévoir un inventaire des versions directes/transitives et un audit NuGet reproductible avant publication. .NET 10 modifie l’audit transitif et l’élagage des packages : il faut interpréter le graphe réellement utilisé, pas conclure à partir d’une seule référence ancienne. [NuGet et .NET 10](https://devblogs.microsoft.com/dotnet/nuget-package-pruning-in-dotnet-10/).

**Avis pour l’architecture :** garder Avalonia/.NET et les services existants. Une migration Electron, web ou vers une base centralisée n’apporte pas de bénéfice démontré pour ces besoins. Les améliorations de recherche peuvent commencer avec un cache en mémoire ; un index persistant ne se justifie qu’après mesure. La signature du logiciel et le suivi des correctifs méritent une étude avant une diffusion plus large. Une signature identifie l’éditeur ; elle n’est pas une garantie universelle d’absence d’alertes Windows.

## Volet 2 — Impression 3D et atelier

### Machines : la réduction des changements et des purges devient un axe majeur

| Technologie / exemple disponible | Fait vérifié chez le constructeur | Intérêt possible pour MS3DPRINT | Critère d’essai décisif |
|---|---|---|---|
| Double buse — Bambu H2D | Buse jusqu’à 350 °C, chambre chauffée à 65 °C ; lancement le 25 mars 2025 | Supports avec une autre matière, séparation de deux filaments | Temps complet et nettoyage sur une vraie pièce avec supports |
| Changement de hotends — Bambu H2C / Vortek | Six hotends interchangeables ; annoncé en novembre 2025 | Réduire les pertes et temps de changement sur certaines pièces multi-matières | Masse totale consommée, purge/tour, fiabilité et temps de changement sur le projet choisi |
| Outils indépendants — Prusa CORE One+ Gen 2 / INDX | Commandes de machines 4/8 outils et premiers kits expédiés annoncés le 20 août 2026 | Dédier des outils aux matières ou diamètres de buse | Répétabilité des changements, qualité aux interfaces, maintenance |
| Double buse compact — Bambu X2D | Buse à 300 °C, chambre à 65 °C et volumes utiles différents selon la buse | Autre compromis encombrement / matériaux / double extrusion | Volume réellement commun aux deux buses, pas uniquement volume maximal publicitaire |

Sources : [H2D](https://blog.bambulab.com/bambu-lab-launches-h2d-to-rethink-personal-manufacturing/), [H2C](https://blog.bambulab.com/bambu-lab-h2c-where-multi-material-vortek-system-meets-engineering-precision/), [INDX en expédition](https://blog.prusa3d.com/prusa-core-one-gen-2-indx-shipping-has-started-complete-printers-open-for-orders_137623/), [X2D](https://blog.bambulab.com/xcellence-made-simple-bambu-lab-presents-the-x2d/).

**Avis :** aucune raison d’acheter uniquement pour la vitesse maximale ou le nombre de couleurs. Comparer trois pièces représentatives : une pièce technique simple, une avec supports difficiles et une petite série. Retenir occupation machine totale, matière consommée, préparation, finition, rebuts et conformité dimensionnelle. Les promesses de suppression des purges dépendent du scénario ; elles ne signifient pas que toute matière hors pièce disparaît. Le parc réel de l’utilisateur n’a pas été inventorié pendant ce bilan.

### Matières : qualifier la référence commerciale, pas seulement son sigle

Le logiciel propose déjà les 26 familles/libellés du guide MS3DPRINT, dont PA6/PA12/PA612 renforcés, PET-CF, PPS et variantes ESD. Cette diversité est suffisante pour commencer une qualification sérieuse : ajouter des références sans essais ne garantit pas une meilleure offre.

La fiche Fiberon PA6-CF20 distingue les méthodes d’essai et les conditions de propriétés ; son comportement à l’humidité et ses températures de déformation sont documentés. Il faut comparer des valeurs mesurées dans des conditions équivalentes, et conserver orientation, conditionnement et éventuel traitement après impression. Une valeur HDT n’est pas à elle seule une température d’usage continu. [Fiche technique Polymaker](https://fiberon.polymaker.com/wp-content/uploads/TDS_FIBERON-PA6-CF20_V1.0_EN.pdf).

**Choix à évaluer :** PET-CF ou PA12/PA612-CF pour certaines pièces techniques ; PPS-CF pour une demande thermique/chimique identifiée ; ESD pour un besoin électrique défini. Ce sont des candidats d’essai, pas des équivalences universelles. Choisir selon charge, choc, température, humidité, tenue dimensionnelle et finitions attendues. Le fabricant doit préciser buse, abrasivité, séchage et paramètres adaptés à chaque référence.

Pour FR et certifications, UL précise que les classements d’un matériau conventionnel Yellow Card ne se transfèrent pas automatiquement à la fabrication additive ; le couple procédé/machine/paramètres doit être évalué. **Un libellé « PC-FR » dans le catalogue ne certifie donc pas la pièce produite.** [UL Solutions](https://www.ul.com/resources/certification-program-plastics-additive-manufacturing-faqs).

### Séchage : une compatibilité souvent mal interprétée

Le tableau Fiberon indique **100 °C pendant 10 h** pour plusieurs références, dont PA6-CF20, PA12-CF10, PA612-CF15, PET-CF17 et PPS-CF10. L’AMS HT annonce un plafond de **85 °C**. **Déduction :** un filament peut être accepté par le chemin d’alimentation tout en nécessitant un autre équipement pour respecter son cycle de séchage prescrit. Il faut vérifier la référence et la bobine, pas généraliser une température à toute la famille. [Tableau Polymaker](https://fiberon.polymaker.com/), [AMS HT constructeur](https://eu.store.bambulab.com/es-es/products/ams-ht).

Prusa rappelle que l’humidité affecte notamment polyamides et flexibles et recommande un stockage adapté. Le maintien au sec pendant l’impression et le séchage préalable sont deux opérations différentes. [Guide Prusa](https://help.prusa3d.com/article/drying-filament_332086?product=xl).

**Application pratique proposée :** une fiche technique par référence dans l’atelier, avec lien TDS, cycle de séchage, buse et profil validé. Ces informations doivent être consultables à la demande ; elles n’ont pas à devenir une série de champs supplémentaires à remplir pour chaque estimation.

### Slicers : stabilité et conservation des profils avant les préversions

| Outil | Version stable repérée au bilan | Évolution utile | Recommandation |
|---|---|---|---|
| Bambu Studio | **2.8.2.61**, 21 août 2026 | Correctifs sur anciens 3MF, profils et certaines connexions LAN | Conserver un profil validé et comparer les résultats avant remplacement |
| OrcaSlicer | **2.4.2**, 7 juillet 2026 | Maintenance des profils, synchronisation et stabilité ; dépôt désormais OrcaSlicer/OrcaSlicer | Bon candidat à une comparaison contrôlée avec le slicer utilisé aujourd’hui |
| PrusaSlicer | **2.9.6**, 25 juin 2026 | ColorMix ; évolution du suivi des consommations de purge et tour dans cette série | Tester les cas multi-matières ; séparer les essais de PrusaSlicer 3 expérimental |

Sources : [Bambu Studio](https://github.com/bambulab/BambuStudio/releases/tag/v02.08.02.61), [OrcaSlicer](https://github.com/OrcaSlicer/OrcaSlicer/releases/tag/v2.4.2), [PrusaSlicer](https://github.com/prusa3d/PrusaSlicer/releases/tag/version_2.9.6), [statut de PrusaSlicer 3](https://www.prusa3d.com/p/prusaslicer/).

**Piste pour le Manager :** récupérer à terme, après sélection et confirmation, les masses, durées et matières issues du fichier du slicer. Conserver leur provenance et préciser si supports/purges sont déjà inclus. Le moteur V2 sait déjà distinguer ces coûts ; une importation réduirait la ressaisie. Elle devra gérer les formats et versions de chaque slicer, sans attribuer à un aperçu géométrique la précision d’un découpage réel.

### Formats, CAO, scan et IA

**3MF : à privilégier pour conserver le contexte de fabrication lorsque le flux le permet.** Le format spécifie unités, assemblages, matériaux et extensions. Le Core 3MF et les paramètres privés d’un slicer ne sont toutefois pas la même chose. Le Manager lit déjà STL/OBJ/3MF ; son lecteur possède des limites de taille et d’assemblage (`ThreeMfReader.cs`). L’étape utile est de reconnaître ce qui est compris, et d’éviter de modifier les informations non prises en charge. [Spécification 3MF](https://github.com/3MFConsortium/spec_core/blob/master/3MF%20Core%20Specification.md).

**CAO :** FreeCAD **1.1.4**, publié le 28 septembre 2026, annonce deux corrections de sécurité lors de l’ouverture de FCStd spécialement construits. Si FreeCAD est utilisé dans l’atelier, sa version mérite d’être vérifiée. Les préversions suivantes restent un espace d’essai distinct. Conserver le fichier CAO natif comme source et un export d’échange, puis les fichiers de fabrication dérivés. [FreeCAD 1.1.4](https://github.com/FreeCAD/FreeCAD/releases/tag/1.1.4).

**Scan :** les progrès de capture ne remplacent pas la validation dimensionnelle. SHINING 3D distingue exactitude, répétabilité, résolution et exactitude volumétrique. Une spécification de résolution fine ne suffit pas à promettre une tolérance sur toute une pièce. Pour comparer des scanners : même pièce, mêmes cotes de référence et plusieurs répétitions ; intégrer aussi préparation de surface, recalage et reconstruction CAO au temps réel. [Guide constructeur](https://www.shining3d.com/resources-center/guide/3d-scanner-accuracy-precision-resolution-and-volumetric-accuracy).

**IA 3D :** des projets comme Microsoft TRELLIS.2 développent la génération de formes et de textures depuis des images. À tester pour exploration visuelle ou formes décoratives ; pour une pièce fonctionnelle, je maintiendrais un contrôle humain des cotes, parois, assemblages et conditions d’utilisation. La génération d’un maillage ne démontre pas sa conformité mécanique. [Projet Microsoft](https://github.com/microsoft/TRELLIS.2).

### Autres procédés et sous-traitance

SLS et MJF restent des pistes pour des pièces ou séries difficiles à réaliser avec les moyens internes. Formlabs documente pour le SLS des contraintes de poudre, de renouvellement et d’atmosphère selon la matière. Materialise propose du PA12 MJF avec des limites géométriques et des propriétés qui dépendent des conditions de fabrication. **Mon avis :** comparer d’abord une pièce représentative sous-traitée ; demander tolérances, finition, contrôle et délai, plutôt qu’envisager immédiatement l’achat d’un système poudre. [Poudres Formlabs](https://formlabs.com/support/Choosing-the-right-powder/), [PA12 MJF Materialise](https://www.materialise.com/en/industrial/3d-printing-materials/pa12-mjf).

La résine technique progresse aussi : Tough 2000 V2 de Formlabs est proposée pour prototypes fonctionnels et pièces finales avec des caractéristiques publiées. L’intérêt doit être évalué avec le lavage, le post-traitement, la géométrie et la tenue attendue, pas seulement l’aspect de surface. [Formlabs](https://formlabs.com/uk/products/tough-2000-resin/).

### Environnement et interopérabilité

L’INRS rappelle que les étapes annexes — préparation, récupération, finition et maintenance — peuvent être plus exposantes que l’impression. Circuit fermé, captage et organisation du travail font partie des moyens de prévention. Ce sujet doit accompagner une montée en gamme vers poudres, résines ou davantage de matières techniques. Il ne s’agit pas ici d’un diagnostic de l’installation réelle. [INRS ED 148](https://www.inrs.fr/media.html?refINRS=ED+148).

Bambu documente des mécanismes d’autorisation, Bambu Connect et un mode développeur LAN. Une intégration réseau dépend donc du modèle et du firmware ; ce bilan ne recommande ni contournement ni changement de firmware. L’échange de fichiers est une piste plus limitée et plus simple pour le Manager, le suivi de production restant différé. [Livre blanc constructeur](https://cdn1.bambulab.com/trust-center/file/bambulab-security-whitepaper-en.pdf).

## Priorités proposées

| Ordre | Action | Bénéfice visé | Preuve à obtenir avant généralisation |
|---|---|---|---|
| 1 | Évaluer Avalonia 12.1.4 et les correctifs de dépendances | Fiabilité des menus, saisies et changements d’écrans | Suite existante, tests ciblés, comparaison mémoire/focus |
| 2 | Réaliser les filtres fichiers, les récents et les aperçus acceptés | Moins de clics pour retrouver une pièce | Parcours clavier/souris, fichiers disparus, deux largeurs |
| 3 | Mesurer les gros dossiers puis limiter les lignes créées | Fluidité sur de vrais volumes | Mesures répétables sur les mêmes données et la même machine |
| 4 | Qualifier séchage et profils de quelques matières utiles | Moins d’échecs et meilleure répétabilité | TDS, cycles respectés, pièce témoin, cotes et état de surface |
| 5 | Comparer les slicers sur trois pièces métier | Temps et consommation plus fiables | Données prévisionnelles comparées aux résultats réels |
| 6 | Étudier l’import des données du slicer | Réduire la ressaisie dans l’estimateur | Sources, unités et absence de double comptage |
| 7 | Préparer la distribution du logiciel | Éditeur identifiable et dépendances suivies | Audit transitif et essai de signature, sans changer la livraison unique |
| 8 | Tester nouvelles machines, scan ou procédés externes sur besoin concret | Élargir l’offre avec un risque maîtrisé | Échantillon conforme, temps complet et conditions de fourniture |

Les points 1 à 3 prolongent directement le logiciel actuel. Les points 4 à 6 peuvent améliorer le travail de l’atelier et la qualité de ses estimations. Les nouveaux équipements et procédés deviennent pertinents lorsqu’un essai métier en montre l’intérêt. Aucun gain chiffré ni retour sur investissement n’est annoncé sans ces essais.
