# Comprendre chaque champ de l’estimateur

Au quotidien, saisir seulement les informations propres à la commande. Les paramètres réutilisables sont enregistrés une fois dans **Paramètres → Atelier**. Le bouton d’information de l’estimateur ouvre ce guide dans le logiciel.

La saisie devient automatiquement un **brouillon**, même si certains champs ne sont pas encore valides. Le dernier brouillon de l’atelier est récupéré à la prochaine ouverture ; les autres restent dans l’historique. **Enregistrer le devis** crée ou met à jour le devis et retire uniquement son brouillon. **Nouveau devis** demande confirmation avant d’abandonner le brouillon actif. L’icône de copie dans l’historique prépare un devis indépendant sans changer l’original ; la copie n’est enregistrée qu’après votre validation.

Tout se renseigne sur **une seule page** : choisir la prestation et reporter les données du slicer ; le prix se met à jour automatiquement à droite. **Ajouter des frais** ouvre les frais complémentaires, qui restent comptés lorsque la section est repliée. Le prix manuel et la remise se modifient directement sous le total. **Enregistrer le devis** ou **Ctrl+Entrée** enregistre sans étape intermédiaire. Les coûts et la rentabilité détaillés restent consultables dans les sections repliables.
Les nouvelles estimations utilisent directement la V2, sans choix de moteur. Les anciens devis gardent leur résultat ; le bouton « Convertir ce devis en V2 » est réservé à leur conversion explicite. Le nom du devis et le projet associé se renseignent en premier, sur la même rangée. Un nom est proposé automatiquement si vous ne renseignez pas le vôtre.

## Chaque commande

| Champ | À quoi il sert |
|---|---|
| Nom du devis | Un titre pour retrouver ce devis. Exemple : 40 supports de bobine. |
| Projet associé | Lien facultatif avec un projet existant, identifié par son nom, sa référence et son client. Ce choix ne modifie pas le prix. |
| Type de prestation | Impression, modélisation seule ou les deux. La modélisation seule n’ajoute ni matière ni temps machine. |
| Filament | La matière utilisée. Son prix au kilo vient de la fiche filament : inutile de le ressaisir dans chaque devis. |
| Machine utilisée | Choisir l’imprimante réellement utilisée. Sans choix précis, le logiciel utilise une moyenne pondérée du parc actif. Il n’additionne pas les taux des machines. |
| Quantité | Le nombre total de pièces conformes à remettre au client, pas le nombre de tentatives ni de plateaux. |
| Poids et durée saisis | Une pièce : le poids et la durée seront multipliés par la quantité. Ensemble / plateau : ils décrivent le lot saisi, sans multiplication par les pièces qu’il contient. |
| Pièces par plateau | En mode ensemble, indiquer combien de pièces contient un plateau. 40 pièces à 10 par plateau donnent 4 plateaux. Laisser 0 si les données couvrent déjà toute la commande. Un dernier plateau incomplet garde les données du plateau complet. |
| Poids (g) | La consommation de filament donnée par le slicer pour une pièce ou un plateau, selon le mode choisi. Inclure supports et purges s’ils sont déjà dans le total du slicer. |
| Durée (heures et minutes) | Le temps donné par le slicer pour la même pièce ou le même plateau que le poids. Pour 1 h 30, saisir 1 dans les heures et 30 dans les minutes. Les minutes doivent rester inférieures à 60. |
| Amortissement retenu (€/h) | Valeur automatiquement reprise de la fiche imprimante ou de la moyenne du parc. L’électricité et la maintenance sont comptées séparément. Modifier la fiche imprimante pour changer le taux des prochains devis. |
| Électricité par passage (€) | Calculée depuis la puissance, le temps et le prix du kWh. Si la puissance est inconnue, saisir le coût électrique de la pièce ou du plateau. Avec plusieurs machines, la saisie manuelle ne couvre que celles dont la puissance manque. |
## Coûts de la commande

| Champ | À quoi il sert |
|---|---|
| Conception / modélisation (€) | Coût de votre travail CAO pour la commande. Exemple : 2 h de travail à un coût de 25 €/h donnent 50 €. Par défaut, ce poste est compté une seule fois. |
| Main-d’œuvre (€) | Coût total des manipulations, chargements ou surveillance pour toute la commande, avant provision pour échecs. Ne pas ressaisir ici le même travail que dans conception ou finitions. |
| Finitions (€) | Total du retrait des supports, ponçage, assemblage ou contrôle de toutes les pièces. Exemple : 2 € de travail sur chacune des 40 pièces = saisir 80 €. Aucune multiplication supplémentaire. |
| Consommables (€) | Total de colle, alcool, protection et autres fournitures pour la fabrication prévue, avant provision pour échecs. Le filament est déjà calculé séparément. |
| Emballage / expédition (€) | Le coût de carton, protection et transport pour la commande. Par défaut, compté une seule fois et non répété en cas d’échec d’impression. |
| Autres coûts (€) | Un coût non déjà saisi ailleurs. Par défaut, une seule fois pour la commande. Laisser 0 si rien ne s’applique. |
| Prestations supplémentaires | Nom et montant total pour toute la commande. Cocher « À refaire si l’impression échoue » uniquement si le coût doit être engagé de nouveau. Décocher « Inclure ce poste » pour l’exclure. |
| Recettes concernées par ce devis | Choisir la rubrique de CA utilisée pour facturer cette commande : vente, prestation BIC ou prestation BNC. Être une entreprise mixte n’oblige pas à mettre tous ses devis en mixte. |
| Part du prix en ventes (%) | Uniquement pour un devis mixte. C’est la part du PRIX FINAL de ce devis, pas votre répartition annuelle. Exemple : 150 € d’objets et 50 € de prestation correspondent à 75 % de ventes. |
| Part du prix en services BIC (%) | La part du prix final affectée aux services artisanaux ou commerciaux. Dans l’exemple 150 € d’objets + 50 € de service BIC, saisir 25 %. Le reste éventuel est affecté au BNC. |
| Remise commerciale (%) | Une réduction sur le prix conseillé ou le prix commercial saisi. Les charges et le résultat sont recalculés après remise. Une alerte apparaît si le prix devient déficitaire. |
| Prix commercial avant remise (€) | Facultatif : votre prix proposé au client pour TOUTE la commande. Vide = prix conseillé. La remise s’applique ensuite sur ce montant. |
## Paramètres → Atelier : une fois

| Champ | À quoi il sert |
|---|---|
| Barème de cotisations | Préremplit les taux officiels de l’année indiquée. Les profils standards France 2026 sont sans ACRE, avec ou sans Cipav pour les BNC. Un autre régime reste possible en saisie personnalisée. Choisir un barème ne modifie pas les anciens devis. |
| Taux ventes / services BIC / services BNC | Pourcentages des recettes affectées à chaque catégorie, pas pourcentages de votre bénéfice. BIC désigne les services artisanaux ou commerciaux ; BNC les prestations libérales. La catégorie dépend de l’activité déclarée, pas du seul usage d’une imprimante 3D. |
| Contribution formation (CFP) | S’ajoute aux cotisations sociales. Choisir commerciale, artisanale ou libérale selon l’activité déclarée. Le barème propose les taux de référence ; ce choix n’est pas déduit automatiquement d’une vente ou d’un service. |
| CFP et autres contributions (%) | Le total des contributions proportionnelles supplémentaires retenues. La CFP choisie préremplit ce champ : ne pas l’ajouter une seconde fois. L’impôt personnel et le versement libératoire de l’impôt ne sont pas calculés automatiquement. |
| Commission de paiement / plateforme (%) | Le pourcentage prélevé sur votre prix final par le service de paiement ou de vente. Laisser 0 sans commission ; décocher pour exclure ce poste. |
| Prix de l’électricité (€/kWh) | Le tarif réellement payé au fournisseur d’électricité. À saisir une fois : il sert à convertir puissance × durée en euros. |
| Méthode de prix | Marge cible : le calcul vise le pourcentage choisi du prix de vente après les coûts et charges retenus. Majoration : augmente simplement le coût d’un pourcentage ; ce n’est pas la même marge sur le chiffre d’affaires. |
| Marge cible / majoration (%) | En marge cible, 20 signifie viser un résultat égal à 20 % du prix de vente après les charges retenues. Charges + marge cible doivent rester sous 100 %. Ce résultat n’est pas un bénéfice net fiscal. |
| Taux d’échec (%) | La proportion moyenne de tentatives qui échouent. 5 signifie 5 %, non 5 pièces. Seuls les coûts classés par tentative sont provisionnés pour les reprises. |
| Forfait par pièce (€) | Montant appliqué à chaque pièce conforme. Coût réel : entre dans le coût avant le calcul du prix. Supplément commercial : s’ajoute au prix conseillé, puis compte dans les charges finales et dans la remise. |
| Frais généraux par commande (€) | Une part d’assurance, de CFE, de loyer ou d’abonnement affectée à une commande. Exemple : répartir un budget annuel sur le nombre prévu de commandes. Ne pas y saisir le budget annuel entier. |
| Frais à prévoir en cas d’échec | Dans l’atelier, cocher uniquement les frais à engager de nouveau si une impression échoue. Le devis attend toujours le total pour la commande. La provision pour échecs est ajoutée séparément. |
## Fiches imprimantes et filaments

| Champ | À quoi il sert |
|---|---|
| Prix d’achat / années / heures productives | Amortissement = prix d’achat ÷ (années d’utilisation × heures productives annuelles). Les heures sont les heures où la machine produit, pas 24 h × 365 si elle reste souvent arrêtée. Les années et les heures annuelles doivent être supérieures à zéro. Les anciennes fiches sans heures annuelles sont proposées en taux manuel avec leur coût horaire conservé. Pour passer au calcul automatique, désactiver le taux manuel et renseigner la production annuelle réelle. |
| Taux horaire manuel | Remplace l’amortissement calculé s’il est activé. Il ne s’ajoute pas une seconde fois à ce dernier. Indiquer seulement l’amortissement ; énergie et maintenance ont leurs propres postes. |
| Maintenance / usure (€/h) | Provision pour l’usure et l’entretien par heure imprimée, configurée dans la fiche machine. Ne pas y inclure une dépense déjà comprise dans un autre poste. |
| Puissance (W) | Consommation électrique moyenne en impression de la machine. 200 W pendant 2 h représentent 0,4 kWh. La puissance est configurée dans la fiche imprimante. |
| Prix matière (€/kg) | Le prix d’achat réellement supporté pour 1 kg. Il est défini dans la fiche filament. En franchise de TVA, tenir compte de la TVA d’achat non récupérée. |
## Options de production

| Champ | À quoi il sert |
|---|---|
| Poids slicer avec supports et purges | Laisser coché si le poids du slicer comprend déjà les supports et purges. Décocher uniquement si vous devez ajouter leur poids séparément. |
| Supports et purges supplémentaires (g) | Poids supplémentaire pour la même pièce ou le même plateau que les données principales. Ignoré si le poids du slicer les inclut déjà, afin d’éviter le double comptage. |
| Répartition sur plusieurs machines | Renseigner le total d’heures de la commande pour chaque machine, avant échecs. La somme doit égaler l’occupation nominale prévue. Chaque machine utilise son taux, sa maintenance et sa puissance. Les taux de référence sont alors remplacés. |
## Résultats

Pour une fabrication sous-traitée, cocher « Fabrication confiée à un fournisseur ». Le coût d’achat et le transport sont les totaux pour toute la commande ; un transport déjà compris dans l’achat reste à zéro. La quantité sert au prix unitaire, sans multiplier à nouveau le devis fournisseur. Ajouter votre propre travail et la livraison au client dans les compléments. Le calcul reprend les charges et la marge de votre atelier ; les coûts et échecs d’impression internes sont exclus.

| Champ | À quoi il sert |
|---|---|
| Coût de revient corrigé | Vos coûts de matière, machine, énergie, travail et autres frais, plus la provision pour reprendre les tentatives ratées. |
| Prix plancher | Prix couvrant les coûts et les charges retenus, avec un résultat nul. Descendre en dessous déclenche un avertissement. |
| Prix conseillé | Prix issu du coût corrigé et de la méthode de marge de l’atelier, plus le supplément commercial éventuel. |
| Prix commercial final | Le prix proposé pour la commande après votre éventuel ajustement et votre remise. |
| Résultat estimé et marge réelle | Prix final moins coûts corrigés et charges retenues. La marge réelle exprime ce résultat en pourcentage du CA final. L’impôt personnel et les postes non renseignés n’y sont pas déduits. |
| Prix et résultat par pièce | Le prix final de la commande, ou son résultat estimé, divisé par la quantité conforme. Ce n’est pas le prix d’un plateau. |

## Barèmes France 2026

Profils proposés sur choix de l’utilisateur, sans ACRE : ventes 12,3 %, prestations BIC 21,2 %, BNC hors Cipav 25,6 %, BNC Cipav 23,2 %. La CFP est distincte : commerciale 0,1 %, artisanale 0,3 %, libérale 0,2 %. Elle dépend de l’activité déclarée et ne se déduit pas du seul contenu d’un devis.

- [Cotisations : Service Public, régime micro-social](https://entreprendre.service-public.gouv.fr/vosdroits/F37353)
- [CFP : Service Public, contribution formation](https://entreprendre.service-public.gouv.fr/vosdroits/F23459)

La franchise en base de TVA ne dispense pas des cotisations sociales. Les taux standards ne couvrent pas tous les régimes particuliers ; les taux restent personnalisables. Le calcul ne remplace pas la qualification de vos activités déclarées ni un calcul d’impôt personnel.

## Réglages propres à chaque atelier

Le fichier `.ms3dprint-manager/estimate-workshop-settings.json` est conservé dans l’espace de travail. Un exécutable partagé ne contient pas les préférences personnelles de son auteur. Sans réglage enregistré, le logiciel conserve des taux neutres personnalisables et propose les catalogues annuels.

Un devis sauvegarde ses taux et ses hypothèses de calcul. Modifier l’atelier change les valeurs des nouveaux devis ; cela ne recalcule pas les devis enregistrés. Lors d’un changement d’espace de travail, l’estimateur repart sur les réglages du nouvel atelier.
