using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop;

public sealed record EstimateHelpEntry(string Group, string Key, string Label, string Explanation);

/// <summary>Shared by input hints and the in-app field guide; contains no user-specific business settings.</summary>
public static class EstimateFieldHelp
{
    public static IReadOnlyList<EstimateHelpEntry> Entries { get; } =
    [
        new("Chaque commande", "EstimateName", "Nom du devis", "Un titre pour retrouver ce devis. Exemple : 40 supports de bobine."),
        new("Chaque commande", "EstimateProject", "Projet associé", "Lien facultatif avec un projet existant, identifié par son nom, sa référence et son client. Ce choix ne modifie pas le prix."),
        new("Chaque commande", "EstimateScope", "Type de prestation", "Impression, modélisation seule ou les deux. La modélisation seule n’ajoute ni matière ni temps machine."),
        new("Chaque commande", "EstimateFilament", "Filament", "La matière utilisée. Son prix au kilo vient de la fiche filament : inutile de le ressaisir dans chaque devis."),
        new("Chaque commande", "_estimatePrinter", "Machine utilisée", "Choisir l’imprimante réellement utilisée. Sans choix précis, le logiciel utilise une moyenne pondérée du parc actif. Il n’additionne pas les taux des machines."),
        new("Chaque commande", "EstimateQuantity", "Quantité", "Le nombre total de pièces conformes à remettre au client, pas le nombre de tentatives ni de plateaux."),
        new("Chaque commande", "EstimatePrintDataMode", "Poids et durée saisis", "Une pièce : le poids et la durée seront multipliés par la quantité. Ensemble / plateau : ils décrivent le lot saisi, sans multiplication par les pièces qu’il contient."),
        new("Chaque commande", "_estimateCapacity", "Pièces par plateau", "En mode ensemble, indiquer combien de pièces contient un plateau. 40 pièces à 10 par plateau donnent 4 plateaux. Laisser 0 si les données couvrent déjà toute la commande. Un dernier plateau incomplet garde les données du plateau complet."),
        new("Chaque commande", "EstimateWeight", "Poids (g)", "La consommation de filament donnée par le slicer pour une pièce ou un plateau, selon le mode choisi. Inclure supports et purges s’ils sont déjà dans le total du slicer."),
        new("Chaque commande", "EstimateHours", "Durée (heures et minutes)", "Le temps d’impression donné par le slicer, pour la même pièce ou le même plateau que le poids. Pour 1 h 30, saisir 1 dans les heures et 30 dans les minutes. Les minutes doivent rester inférieures à 60."),
        new("Chaque commande", "EstimateMachineRate", "Amortissement retenu (€/h)", "Valeur automatiquement reprise de la fiche imprimante ou de la moyenne du parc. L’électricité et la maintenance sont comptées séparément. Modifier la fiche imprimante pour changer le taux des prochains devis."),
        new("Chaque commande", "EstimateElectricity", "Électricité par passage (€)", "Calculée depuis la puissance, le temps et le prix du kWh. Si la puissance est inconnue, saisir le coût électrique de la pièce ou du plateau. Avec plusieurs machines, la saisie manuelle ne couvre que celles dont la puissance manque."),
        new("Coûts de la commande", "EstimateDesign", "Conception / modélisation (€)", "Coût de votre travail CAO pour la commande. Exemple : 2 h de travail à un coût de 25 €/h donnent 50 €. Par défaut, ce poste est compté une seule fois."),
        new("Coûts de la commande", "EstimateLabor", "Main-d’œuvre (€)", "Montant total des manipulations, chargements ou surveillance pour toute la commande, avant provision pour échecs. Ne pas compter le même travail dans conception ou finitions. Les réglages de l’atelier indiquent si ces frais sont à engager de nouveau après un échec."),
        new("Coûts de la commande", "EstimatePostProcessing", "Finitions (€)", "Coût total du retrait des supports, ponçage, assemblage ou contrôle de toutes les pièces. Exemple : 2 € de travail sur chacune des 40 pièces = saisir 80 €. Aucune multiplication supplémentaire par la quantité."),
        new("Coûts de la commande", "EstimateConsumables", "Consommables (€)", "Total de colle, alcool, protection et autres fournitures pour toute la fabrication prévue, avant provision pour échecs. Ne pas inclure le filament déjà calculé. Les réglages de l’atelier indiquent si ces frais sont à engager de nouveau après un échec."),
        new("Coûts de la commande", "EstimatePackaging", "Emballage / expédition (€)", "Le coût de carton, protection et transport pour la commande. Par défaut, compté une seule fois et non répété en cas d’échec d’impression."),
        new("Coûts de la commande", "EstimateMisc", "Autres coûts (€)", "Un coût non déjà saisi ailleurs. Par défaut, une seule fois pour la commande. Laisser 0 si rien ne s’applique."),
        new("Coûts de la commande", "ExtraCosts", "Prestations supplémentaires", "Ajouter un scan, une préparation ou une autre intervention : saisir son nom et son montant total pour toute la commande. Cocher « À refaire si l’impression échoue » uniquement si ce coût doit être engagé de nouveau. Décocher « Inclure ce poste » pour l’exclure."),
        new("Coûts de la commande", "_estimateChargeCategory", "Recettes concernées par ce devis", "Choisir la rubrique de CA utilisée pour facturer cette commande : vente, prestation BIC ou prestation BNC. Être une entreprise mixte n’oblige pas à mettre tous ses devis en mixte."),
        new("Coûts de la commande", "_estimateSalesShare", "Part du prix en ventes (%)", "Uniquement pour un devis mixte. C’est la part du PRIX FINAL de ce devis, pas votre répartition annuelle. Exemple : 150 € d’objets et 50 € de prestation correspondent à 75 % de ventes."),
        new("Coûts de la commande", "_estimateBicShare", "Part du prix en services BIC (%)", "La part du prix final affectée aux services artisanaux ou commerciaux. Dans l’exemple 150 € d’objets + 50 € de service BIC, saisir 25 %. Le reste éventuel est affecté au BNC."),
        new("Coûts de la commande", "EstimateDiscount", "Remise commerciale (%)", "Une réduction sur le prix conseillé ou le prix commercial saisi. Les charges et le résultat sont recalculés après remise. Une alerte apparaît si le prix devient déficitaire."),
        new("Coûts de la commande", "_estimateCommercial", "Prix commercial avant remise (€)", "Facultatif : votre prix proposé au client pour TOUTE la commande. Vide = prix conseillé. La remise s’applique ensuite sur ce montant."),
        new("Paramètres → Atelier : une fois", "Preset", "Barème de cotisations", "Préremplit les taux officiels de l’année indiquée. Les profils standards France 2026 sont sans ACRE, avec ou sans Cipav pour les BNC. Un autre régime reste possible en saisie personnalisée. Choisir un barème ne modifie pas les anciens devis."),
        new("Paramètres → Atelier : une fois", "SocialRates", "Taux ventes / services BIC / services BNC", "Pourcentages des recettes affectées à chaque catégorie, pas pourcentages de votre bénéfice. BIC désigne les services artisanaux ou commerciaux ; BNC les prestations libérales. La catégorie dépend de l’activité déclarée, pas du seul usage d’une imprimante 3D."),
        new("Paramètres → Atelier : une fois", "Training", "Contribution formation (CFP)", "S’ajoute aux cotisations sociales. Choisir commerciale, artisanale ou libérale selon l’activité déclarée. Le barème propose les taux de référence ; ce choix n’est pas déduit automatiquement d’une vente ou d’un service."),
        new("Paramètres → Atelier : une fois", "Contributions", "CFP et autres contributions (%)", "Le total des contributions proportionnelles supplémentaires retenues. La CFP choisie préremplit ce champ : ne pas l’ajouter une seconde fois. L’impôt personnel et le versement libératoire de l’impôt ne sont pas calculés automatiquement."),
        new("Paramètres → Atelier : une fois", "Commission", "Commission de paiement / plateforme (%)", "Le pourcentage prélevé sur votre prix final par le service de paiement ou de vente. Laisser 0 sans commission ; décocher pour exclure ce poste."),
        new("Paramètres → Atelier : une fois", "EstimateKwhPrice", "Prix de l’électricité (€/kWh)", "Le tarif réellement payé au fournisseur d’électricité. À saisir une fois : il sert à convertir puissance × durée en euros."),
        new("Paramètres → Atelier : une fois", "PricingMode", "Méthode de prix", "Marge cible : le calcul vise le pourcentage choisi du prix de vente après les coûts et charges retenus. Majoration : augmente simplement le coût d’un pourcentage ; ce n’est pas la même marge sur le chiffre d’affaires."),
        new("Paramètres → Atelier : une fois", "EstimateMargin", "Marge cible / majoration (%)", "En marge cible, 20 signifie viser un résultat égal à 20 % du prix de vente après les charges retenues. Charges + marge cible doivent rester sous 100 %. Ce résultat n’est pas un bénéfice net fiscal."),
        new("Paramètres → Atelier : une fois", "EstimateFailure", "Taux d’échec (%)", "La proportion moyenne de tentatives qui échouent. 5 signifie 5 %, non 5 pièces. Seuls les coûts classés par tentative sont provisionnés pour les reprises."),
        new("Paramètres → Atelier : une fois", "EstimateMinimum", "Forfait par pièce (€)", "Montant appliqué à chaque pièce conforme. Coût réel : entre dans le coût avant le calcul du prix. Supplément commercial : s’ajoute au prix conseillé, puis compte dans les charges finales et dans la remise."),
        new("Paramètres → Atelier : une fois", "GeneralFixed", "Frais généraux par commande (€)", "Une part d’assurance, de CFE, de loyer ou d’abonnement affectée à une commande. Exemple : répartir un budget annuel sur le nombre prévu de commandes. Ne pas y saisir le budget annuel entier."),
        new("Paramètres → Atelier : une fois", "Frequencies", "Frais à prévoir en cas d’échec", "Tous les montants saisis dans le devis sont des totaux pour la commande. Dans l’atelier, cocher uniquement les frais à engager de nouveau si une impression échoue. Le logiciel ajoute leur provision pour échecs, sans multiplier les montants par la quantité."),
        new("Fiches imprimantes et filaments", "PrinterPurchase", "Prix d’achat / années / heures productives", "Amortissement = prix d’achat ÷ (années d’utilisation × heures productives annuelles). Les heures sont les heures où la machine produit, pas 24 h × 365 si elle reste souvent arrêtée. Les anciennes durées de vie en heures restent utilisables."),
        new("Fiches imprimantes et filaments", "PrinterManualRate", "Taux horaire manuel", "Remplace l’amortissement calculé s’il est activé. Il ne s’ajoute pas une seconde fois à ce dernier. Indiquer seulement l’amortissement ; énergie et maintenance ont leurs propres postes."),
        new("Fiches imprimantes et filaments", "_estimateMaintenance", "Maintenance / usure (€/h)", "Provision pour l’usure et l’entretien par heure imprimée, configurée dans la fiche machine. Ne pas y inclure une dépense déjà comprise dans un autre poste."),
        new("Fiches imprimantes et filaments", "_estimatePower", "Puissance (W)", "Consommation électrique moyenne en impression de la machine. 200 W pendant 2 h représentent 0,4 kWh. La puissance est configurée dans la fiche imprimante."),
        new("Fiches imprimantes et filaments", "_estimateFilamentPrice", "Prix matière (€/kg)", "Le prix d’achat réellement supporté pour 1 kg. Il est défini dans la fiche filament. En franchise de TVA, tenir compte de la TVA d’achat non récupérée."),
        new("Options de production", "_estimateWeightIncludesSupports", "Poids slicer avec supports et purges", "Laisser coché si le poids du slicer comprend déjà les supports et purges. Décocher uniquement si vous devez ajouter leur poids séparément."),
        new("Options de production", "_estimateExtraGrams", "Supports et purges supplémentaires (g)", "Poids supplémentaire pour la même pièce ou le même plateau que les données principales. Ignoré si le poids du slicer les inclut déjà, afin d’éviter le double comptage."),
        new("Options de production", "Allocations", "Répartition sur plusieurs machines", "Renseigner le total d’heures de la commande pour chaque machine, avant échecs. La somme doit égaler l’occupation nominale prévue. Chaque machine utilise son taux, sa maintenance et sa puissance. Les taux de référence sont alors remplacés."),
        new("Résultats", "CorrectedCost", "Coût de revient corrigé", "Vos coûts de matière, machine, énergie, travail et autres frais, plus la provision pour reprendre les tentatives ratées."),
        new("Résultats", "FloorPrice", "Prix plancher", "Prix couvrant les coûts et les charges retenus, avec un résultat nul. Descendre en dessous déclenche un avertissement."),
        new("Résultats", "RecommendedPrice", "Prix conseillé", "Prix issu du coût corrigé et de la méthode de marge de l’atelier, plus le supplément commercial éventuel."),
        new("Résultats", "FinalPrice", "Prix commercial final", "Le prix proposé pour la commande après votre éventuel ajustement et votre remise."),
        new("Résultats", "Profit", "Résultat estimé et marge réelle", "Prix final moins coûts corrigés et charges retenues. La marge réelle exprime ce résultat en pourcentage du CA final. L’impôt personnel et les postes non renseignés n’y sont pas déduits."),
        new("Résultats", "UnitResults", "Prix et résultat par pièce", "Le prix final de la commande, ou son résultat estimé, divisé par la quantité conforme. Ce n’est pas le prix d’un plateau.")
    ];
    public static void Attach(Control input, string key)
    {
        var help = Entries.FirstOrDefault(e => e.Key == key || e.Label == key)?.Explanation;
        if (help is null) return;
        ToolTip.SetTip(input, new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });
        Avalonia.Automation.AutomationProperties.SetHelpText(input, help);
    }
}

public sealed class EstimateHelpWindow : DialogWindow
{
    public EstimateHelpWindow()
    {
        Title = "Comprendre l’estimateur"; Width = 650; Height = 850; MinWidth = 440;
        var body = new List<Control>();
        foreach (var group in EstimateFieldHelp.Entries.GroupBy(e => e.Group))
        {
            body.Add(new TextBlock { Text = group.Key, FontWeight = FontWeight.SemiBold, FontSize = 16, Margin = new Avalonia.Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap });
            foreach (var item in group)
                body.Add(new StackPanel { Spacing = 6, Children = { new TextBlock { Text = item.Label, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = item.Explanation, LineHeight = 22, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } } } });
        }
        var close = new Button { Content = "Fermer" }; close.Click += (_, _) => Close(true);
        Layout("Comprendre chaque champ", "Au quotidien : choisir matière et machine, saisir quantité, poids, durée et les prestations. Les réglages réutilisables sont dans Paramètres → Atelier.", body, close, cancel: new Button { IsVisible = false });
    }
}
