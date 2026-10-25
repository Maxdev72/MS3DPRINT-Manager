using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Estimates;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Reusable workshop defaults. These controls never edit a quote already in progress.</summary>
public sealed class WorkshopEstimateWindow : DialogWindow
{
    private readonly EstimateChargeCatalog _catalog = EstimateChargeCatalog.Load();
    private readonly WorkshopEstimateSettings _original;
    private readonly ComboBox _preset = new() { PlaceholderText = "Personnalisé / autre régime", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _training = new() { PlaceholderText = "Choisir selon l’activité déclarée", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _social = new() { Content = "Inclure les cotisations sociales" };
    private readonly CheckBox _contributions = new() { Content = "Inclure la CFP et les autres contributions" };
    private readonly CheckBox _commercial = new() { Content = "Inclure les commissions de paiement / plateforme" };
    private readonly TextBox _sales = Number(), _bic = Number(), _bnc = Number(), _cfp = Number(), _commission = Number();
    private readonly TextBox _kwh = Number(), _margin = Number(), _failure = Number(), _fee = Number(), _fixed = Number();
    private readonly ComboBox _pricing = Choice("Marge cible après charges sur le CA", "Majoration sur le coût");
    private readonly ComboBox _feeKind = Choice("Supplément commercial par pièce", "Coût réel par pièce conforme");
    private readonly CheckBox[] _repeatOnFailure = Enumerable.Range(0, 6).Select(_ => new CheckBox()).ToArray();
    private readonly TextBlock _rates = new() { TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    private readonly TextBlock _error = FormKit.ErrorText();
    public WorkshopEstimateSettings? Result { get; private set; }
    private static TextBox Number() => new() { Text = "0", Classes = { "estimate-number" } };
    private static ComboBox Choice(params string[] choices) => new()
    {
        ItemsSource = choices, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((text, _) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap })
    };
    private static string Exact(decimal value) => value.ToString("0.############################", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
    private static TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    private static Control Field(string label, Control input, string? unit = null, string? hint = null)
    {
        var field = FormKit.Field(label, input, unit, hint);
        ((TextBlock)field.Children[0]).TextWrapping = TextWrapping.Wrap;
        field.Spacing = 6;
        if (hint is not null && field.Children.LastOrDefault() is TextBlock explanation) { explanation.FontSize = 13; explanation.LineHeight = 20; }
        return field;
    }
    private static Control Section(string title, string description, params Control[] children)
    {
        var content = new StackPanel { Spacing = 18 };
        foreach (var child in children) content.Children.Add(child);
        return new Expander
        {
            Header = new StackPanel { Spacing = 4, Children =
            {
                new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = description, FontSize = 13, LineHeight = 20, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap }
            } },
            Classes = { "estimate-section" }, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = content
        };
    }

    public WorkshopEstimateWindow(WorkshopEstimateSettings settings)
    {
        _original = settings;
        Title = "Réglages de l’atelier"; Width = 620; Height = 850; MinWidth = 440;
        _preset.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<EstimateChargePreset>((p, _) => new TextBlock { Text = p?.Name, TextWrapping = TextWrapping.Wrap });
        _training.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<EstimateTrainingPreset>((p, _) => new TextBlock { Text = p?.Name, TextWrapping = TextWrapping.Wrap });
        var presetChoices = new[] { new EstimateChargePreset("", "Taux personnalisés / autre régime", 0, 0, 0, 0, "") }.Concat(_catalog.Presets).ToArray();
        _preset.ItemsSource = presetChoices; _preset.SelectedItem = presetChoices.FirstOrDefault(p => p.Id == settings.PresetId) ?? presetChoices[0];
        _training.ItemsSource = _catalog.Training; _training.SelectedItem = _catalog.Training.FirstOrDefault(p => p.RatePercent == settings.Charges.ContributionPercent);
        _social.IsChecked = settings.Charges.SocialEnabled; _contributions.IsChecked = settings.Charges.ContributionsEnabled; _commercial.IsChecked = settings.Charges.CommercialEnabled;
        _sales.Text = Exact(settings.Charges.SalesSocialPercent); _bic.Text = Exact(settings.Charges.ServicesBicSocialPercent); _bnc.Text = Exact(settings.Charges.ServicesBncSocialPercent);
        _cfp.Text = Exact(settings.Charges.ContributionPercent); _commission.Text = Exact(settings.Charges.CommercialPercent);
        _kwh.Text = Exact(settings.ElectricityPricePerKwh); _margin.Text = Exact(settings.MarginPercent); _failure.Text = Exact(settings.FailureRatePercent); _fee.Text = Exact(settings.PieceFee); _fixed.Text = Exact(settings.GeneralFixedCost);
        _pricing.SelectedIndex = (int)settings.PricingMode; _feeKind.SelectedIndex = (int)settings.FeeKind;
        CostFrequency[] frequencies = [settings.DesignFrequency, settings.LaborFrequency, settings.PostProcessingFrequency, settings.ConsumablesFrequency, settings.PackagingFrequency, settings.MiscFrequency];
        for (var i = 0; i < frequencies.Length; i++) _repeatOnFailure[i].IsChecked = frequencies[i] == CostFrequency.PerAttempt;
        var frequencyFields = new List<Control> { Hint("Dans l’estimateur, saisissez toujours le total pour toute la commande. Cochez ici uniquement les frais à engager de nouveau si une impression échoue : ils entrent dans la provision pour échecs.") };
        string[] names = ["Conception", "Main-d’œuvre", "Post-traitement", "Consommables", "Emballage / expédition", "Autres coûts"];
        for (var i = 0; i < names.Length; i++) { _repeatOnFailure[i].Content = names[i]; frequencyFields.Add(_repeatOnFailure[i]); }
        var save = new Button { Content = "Enregistrer pour cet atelier" };
        save.Click += (_, _) => Save();
        Layout("Réglages de l’atelier", "À définir une fois pour les prochains devis de cet espace de travail. Les devis existants gardent leurs propres valeurs.",
        [
            Field("Barème de cotisations", _preset, hint: "Taux standards 2026 sans ACRE. Autre pays, autre régime ou ACRE : saisir vos taux dans les réglages détaillés."),
            _rates,
            Section("Cotisations et contributions", "Formation professionnelle et frais de paiement", _social,
                Field("CFP : nature de l’activité déclarée", _training, hint: "La fabrication ne permet pas de déduire automatiquement votre inscription. Choisir commerciale, artisanale ou libérale selon votre déclaration."),
                _contributions, Field("CFP et autres contributions sur le CA", _cfp, "%", "Le choix de CFP préremplit ce montant. Ajouter ici les autres contributions applicables, sans compter deux fois la CFP."),
                _commercial, Field("Commission de paiement / plateforme", _commission, "%", "Exemple : 2 pour 2 % du prix de vente. Laisser 0 si aucune commission.")),
            Section("Prix et production par défaut", "Électricité, marge, aléas et forfait par pièce",
                Field("Prix de l’électricité réellement payé", _kwh, "€/kWh"),
                Field("Méthode de calcul du prix", _pricing), Field("Marge cible ou majoration", _margin, "%", "En marge cible, 20 signifie viser 20 % du prix de vente après les charges retenues."),
                Field("Taux d’échec moyen", _failure, "%", "5 signifie 5 % de tentatives ratées, uniquement sur les coûts répétables."),
                Field("Forfait par pièce", _fee, "€"), Field("Nature du forfait", _feeKind),
                Field("Frais généraux affectés à une commande", _fixed, "€", "Part d’assurance, CFE, abonnement ou loyer affectée à chaque commande. Ne pas saisir ici une dépense annuelle entière.")),
            Section("Frais à prévoir en cas d’échec", "Réglage automatique de la provision des prochains devis", frequencyFields.ToArray()),
            Section("Taux personnalisés / cas particuliers", "Adapter les taux à votre régime ou à l’ACRE",
                Hint("Les taux sont stockés dans un catalogue annuel distinct du moteur. Ils restent modifiables, notamment pour l’ACRE ou un autre régime. Le BNC Cipav et le BNC hors Cipav ont des taux distincts."),
                Field("Cotisations sur les ventes", _sales, "%"), Field("Cotisations sur les services BIC", _bic, "%"), Field("Cotisations sur les services BNC", _bnc, "%"),
                Hint("Sources : Service Public, régime micro-social F37353 et contribution formation F23459, vérifiés en 2026. La TVA collectée et l’impôt personnel ne sont pas calculés.")),
            _error
        ], save);
        ((Control)Content!).Classes.Add("workshop-form");
        _preset.SelectionChanged += (_, _) =>
        {
            if (_preset.SelectedItem is not EstimateChargePreset p || string.IsNullOrEmpty(p.Id)) return;
            _sales.Text = Exact(p.SalesRate); _bic.Text = Exact(p.ServicesBicRate); _bnc.Text = Exact(p.ServicesBncRate); _social.IsChecked = true; UpdateState();
        };
        _training.SelectionChanged += (_, _) => { if (_training.SelectedItem is EstimateTrainingPreset p) { _cfp.Text = Exact(p.RatePercent); _contributions.IsChecked = true; } };
        foreach (var check in new[] { _social, _contributions, _commercial }) check.IsCheckedChanged += (_, _) => UpdateState();
        foreach (var field in new[] { _sales, _bic, _bnc }) field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) UpdateState(); };
        UpdateState();
    }
    private void UpdateState()
    {
        _rates.LineHeight = 24;
        _rates.Text = $"Ventes : {_sales.Text} %\nPrestations BIC : {_bic.Text} % · Prestations BNC : {_bnc.Text} %";
        if (_preset.SelectedItem is EstimateChargePreset { Year: > 0 } && decimal.TryParse(_cfp.Text, out var cfp) && cfp == 0)
            _rates.Text += "\nCFP à préciser dans Cotisations et contributions.";
        _sales.IsEnabled = _bic.IsEnabled = _bnc.IsEnabled = _social.IsChecked == true;
        _cfp.IsEnabled = _contributions.IsChecked == true; _commission.IsEnabled = _commercial.IsChecked == true;
    }
    private decimal Read(TextBox box)
    {
        if (!decimal.TryParse(box.Text?.Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowLeadingWhite | System.Globalization.NumberStyles.AllowTrailingWhite,
            System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            const string message = "Saisissez des valeurs positives, avec une virgule ou un point.";
            FormKit.ShowError(_error, box, message);
            throw new ArgumentException(message);
        }
        return value;
    }
    private CostFrequency RepeatFrequency(int index) => _repeatOnFailure[index].IsChecked == true ? CostFrequency.PerAttempt : CostFrequency.PerOrder;

    private void Save()
    {
        try
        {
            var c = _original.Charges with { SalesSocialPercent = Read(_sales), ServicesBicSocialPercent = Read(_bic), ServicesBncSocialPercent = Read(_bnc), ContributionPercent = Read(_cfp), CommercialPercent = Read(_commission),
                SocialEnabled = _social.IsChecked == true, ContributionsEnabled = _contributions.IsChecked == true, CommercialEnabled = _commercial.IsChecked == true };
            var preset = _preset.SelectedItem as EstimateChargePreset;
            var id = preset is { Year: > 0 } && preset.SalesRate == c.SalesSocialPercent && preset.ServicesBicRate == c.ServicesBicSocialPercent && preset.ServicesBncRate == c.ServicesBncSocialPercent ? preset.Id : null;
            Result = new(c, id, Read(_kwh), Read(_margin), Read(_failure), Read(_fee), (PieceFeeKind)_feeKind.SelectedIndex, (EstimatePricingMode)_pricing.SelectedIndex, Read(_fixed),
                RepeatFrequency(0), RepeatFrequency(1), RepeatFrequency(2), RepeatFrequency(3), RepeatFrequency(4), RepeatFrequency(5));
            EstimateCalculator.Calculate(new(MarginPercent: Result.MarginPercent, FailureRatePercent: Result.FailureRatePercent, MinimumPerPiece: Result.PieceFee, EngineVersion: 2, V2: Result.Options));
            Close(true);
        }
        catch (Exception e) when (e is ArgumentException or OverflowException) { Result = null; _error.Text = e.Message; _error.IsVisible = true; }
    }
}

