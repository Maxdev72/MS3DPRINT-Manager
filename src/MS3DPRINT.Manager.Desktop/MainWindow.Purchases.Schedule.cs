using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private bool _purchaseScheduleView;
    private bool _expenseMutationBusy;
    private IReadOnlyList<ExpensePlan> _expensePlans = [];
    private IReadOnlyList<string> _expenseReadErrors = [];
    private string? _expenseWorkspace;
    private Guid? _expandedExpensePlan;

    private async void PurchaseView_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        _purchaseScheduleView = tag == "schedule";
        await LoadPurchasesAsync();
    }
    private void ApplyPurchaseView()
    {
        PurchaseRegisterToolbar.IsVisible = !_purchaseScheduleView; PurchaseScheduleToolbar.IsVisible = _purchaseScheduleView;
        PurchaseRegisterViewButton.Classes.Set("selected", !_purchaseScheduleView); PurchaseScheduleViewButton.Classes.Set("selected", _purchaseScheduleView);
    }

    private async Task LoadExpensePlansAsync()
    {
        var root = WorkspaceRoot; var load = StartWorkspaceLoad("purchases", root);
        PurchaseList.Children.Clear();
        PurchaseList.Children.Add(new TextBlock { Text = "Chargement des échéanciers…", Classes = { "muted" } });
        try
        {
            var result = await Task.Run(() => { var store = new ExpensePlanStore(root); var plans = store.LoadReadable(); return (Plans: plans, Errors: store.ReadErrors); });
            if (!IsWorkspaceLoadCurrent(load) || !_purchaseScheduleView) return;
            if (!string.Equals(root, _expenseWorkspace, StringComparison.OrdinalIgnoreCase)) _expandedExpensePlan = null;
            _expenseWorkspace = root; _expensePlans = result.Plans; _expenseReadErrors = result.Errors;
            RenderExpensePlans();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            if (!IsWorkspaceLoadCurrent(load)) return;
            _expensePlans = []; _expenseWorkspace = null;
            PurchaseList.Children.Clear(); PurchaseList.Children.Add(CreateReadErrorState("Échéanciers indisponibles", "Vérifiez le dossier de l’atelier puis actualisez.", e, LoadExpensePlansAsync));
        }
    }

    private void ExpenseFilter_Changed(object? sender, RoutedEventArgs e) => RenderExpensePlans();

    private void RenderExpensePlans()
    {
        if (!_purchaseScheduleView || !string.Equals(_expenseWorkspace, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
        PurchaseList.Children.Clear();
        var archived = ExpenseArchivedBox.IsChecked == true;
        var plans = _expensePlans.Where(p => p.IsArchived == archived).ToArray();
        if (_expandedExpensePlan is null) _expandedExpensePlan = plans.FirstOrDefault()?.Id;
        Control Header()
        {
            var header = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 12) };
            header.Children.Add(new TextBlock { Text = $"{plans.Length} fiche(s) · prévisions et paiements confirmés", FontSize = 16, FontWeight = FontWeight.SemiBold });
            header.Children.Add(new TextBlock { Text = "Les dates passées ne sont pas considérées payées automatiquement. Le registre des achats reste indépendant.", TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
            if (_expenseReadErrors.Count > 0) header.Children.Add(CreateReadErrorState("Certaines fiches sont illisibles", "Les fichiers sont conservés. Les autres échéanciers restent disponibles.", new IOException(string.Join(Environment.NewLine, _expenseReadErrors)), LoadExpensePlansAsync, compact: true));
            return header;
        }
        if (plans.Length == 0)
        {
            PurchaseList.Children.Add(Header());
            PurchaseList.Children.Add(CreateEmptyState("IconBook", archived ? "Aucune fiche archivée" : "Aucune charge planifiée", "Ajoutez un financement à partir des montants de la banque, ou une charge récurrente.", "Ajouter un financement", CreateExpenseFinancing_Click));
            return;
        }
        PagedRows.Render(PurchaseList, plans, BuildExpensePlanCard, Header, (WorkspaceRoot, archived));
    }

    private async void CreateExpenseFinancing_Click(object? sender, RoutedEventArgs e) => await EditExpensePlanAsync(null, ExpensePlanKind.Financing);
    private async void CreateExpenseRecurring_Click(object? sender, RoutedEventArgs e) => await EditExpensePlanAsync(null, ExpensePlanKind.Recurring);

    private async Task EditExpensePlanAsync(ExpensePlan? original, ExpensePlanKind kind)
    {
        var root = WorkspaceRoot; var navigation = CurrentNavigationStop?.Key;
        if (_expenseMutationBusy) return;
        var dialog = new ExpensePlanWindow(kind, plan => MutateExpensePlanAsync(root, navigation, () =>
        {
            var store = new ExpensePlanStore(root);
            if (original is null) { store.Create(plan); return plan; }
            return store.Update(plan, original.UpdatedAt);
        }), original);
        await dialog.ShowDialog<bool>(this);
    }

    private Control BuildExpensePlanCard(ExpensePlan plan)
    {
        var root = WorkspaceRoot; var navigation = CurrentNavigationStop?.Key;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var summary = ExpensePlans.Summarize(plan, today);
        var rows = ExpensePlans.Schedule(plan, today);
        var heading = new StackPanel { Spacing = 5 };
        heading.Children.Add(new TextBlock { Text = plan.Label, FontWeight = FontWeight.SemiBold, FontSize = 19, TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(new TextBlock { Text = plan.Organization + " · " + (plan.Kind == ExpensePlanKind.Financing ? "Financement" : "Charge récurrente") + " · " + (summary.NextDue is { } date ? $"prochaine échéance {date:dd/MM/yyyy}" : "Échéances confirmées"), TextWrapping = TextWrapping.Wrap, Classes = { "caption" } });
        var body = new StackPanel { Spacing = 14 };
        var facts = new StackPanel { Spacing = 0 };
        void Fact(string label, string value, bool strong = false)
        {
            var left = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var right = new TextBlock { Text = value, FontWeight = strong ? FontWeight.Bold : FontWeight.Normal, FontSize = strong ? 17 : 14, TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 14, MinHeight = 36 };
            Grid.SetColumn(right, 1); line.Children.Add(left); line.Children.Add(right);
            facts.Children.Add(new Border { Child = line, BorderBrush = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1) });
        }
        Fact("Organisme", plan.Organization); Fact(plan.Kind == ExpensePlanKind.Financing ? "Équipement" : "Charge", plan.Label);
        Fact(plan.IntervalMonths == 1 ? "Mensualité habituelle" : "Montant habituel", RevenueExporter.Money(plan.RegularAmount), true);
        Fact("Prochaine échéance à payer", summary.NextDue?.ToString("dd/MM/yyyy") ?? "Aucune dans les échéances affichées");
        Fact(summary.IsForecast ? "Prévision jusqu’au" : "Dernière échéance", summary.LastDue?.ToString("dd/MM/yyyy") ?? "—");
        if (!summary.IsForecast) Fact("Dernière mensualité", RevenueExporter.Money(summary.LastAmount ?? plan.RegularAmount));
        Fact(summary.IsForecast ? "Échéances restantes sur la période" : "Échéances restantes", summary.RemainingCount.ToString());
        Fact(summary.IsForecast ? "Total prévu restant sur la période" : "Total restant à payer", RevenueExporter.Money(summary.RemainingAmount), true);
        body.Children.Add(facts);
        var actions = new WrapPanel { ItemSpacing = 10, LineSpacing = 8 };
        var edit = new Button { Content = Icons.Label("IconEdit", "Modifier la fiche"), Classes = { "ghost" } };
        edit.Click += async (_, _) => { if (ExpenseContextCurrent(root, navigation)) await EditExpensePlanAsync(plan, plan.Kind); };
        actions.Children.Add(edit);
        var archive = new Button { Content = plan.IsArchived ? "Restaurer la fiche" : "Archiver la fiche", Classes = { "ghost" } };
        archive.Click += async (_, _) =>
        {
            if (!ExpenseContextCurrent(root, navigation)) return;
            if (!plan.IsArchived && !await new ConfirmRemovalWindow(plan.Label, "La fiche quittera la liste active et restera consultable dans les archives, avec ses paiements. Aucun achat ne sera supprimé.").ShowDialog<bool>(this)) return;
            var error = await MutateExpensePlanAsync(root, navigation, () => new ExpensePlanStore(root).Update(plan with { IsArchived = !plan.IsArchived }, plan.UpdatedAt));
            if (error is not null) Notify(error, error: true);
        };
        actions.Children.Add(archive); body.Children.Add(actions);
        var terms = new StackPanel { Spacing = 5 };
        PagedRows.Render(terms, rows, row =>
        {
            var mark = plan.Payments.SingleOrDefault(m => m.Installment.Id == row.Id);
            var text = new StackPanel { Spacing = 4 };
            text.Children.Add(new TextBlock { Text = $"{row.DueOn:dd/MM/yyyy} · {RevenueExporter.Money(row.Amount)}", FontWeight = FontWeight.SemiBold });
            text.Children.Add(new TextBlock { Text = mark is not null ? $"Payé le {mark.PaidOn:dd/MM/yyyy}" : row.DueOn < today ? "À payer · date dépassée" : "Prévu · paiement non confirmé", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            var parts = new List<string>();
            if (row.Principal is { } capital) parts.Add("Capital " + RevenueExporter.Money(capital));
            if (row.Interest is { } interest) parts.Add("Intérêts " + RevenueExporter.Money(interest));
            if (row.Insurance is { } insurance) parts.Add("Assurance " + RevenueExporter.Money(insurance));
            if (row.RemainingCapital is { } balance) parts.Add("Capital restant dû " + RevenueExporter.Money(balance));
            if (parts.Count > 0) text.Children.Add(new TextBlock { Text = string.Join(" · ", parts), TextWrapping = TextWrapping.Wrap, Classes = { "caption" } });
            var action = new Button { Content = mark is null ? "Confirmer le paiement" : "Annuler la confirmation", Classes = { "ghost" } };
            action.Click += async (_, _) =>
            {
                if (!ExpenseContextCurrent(root, navigation) || _expenseMutationBusy) return;
                if (mark is null)
                    await new ExpensePaymentWindow(plan, row, paidOn => MutateExpensePlanAsync(root, navigation, () => new ExpensePlanStore(root).Update(ExpensePlans.ConfirmPayment(plan, row.Id, paidOn, DateOnly.FromDateTime(DateTime.Today)), plan.UpdatedAt))).ShowDialog<bool>(this);
                else if (await new ConfirmRemovalWindow($"Confirmation du {row.DueOn:dd/MM/yyyy}", "Cette échéance redeviendra à payer. Le registre des achats ne sera pas modifié.").ShowDialog<bool>(this))
                {
                    var error = await MutateExpensePlanAsync(root, navigation, () => new ExpensePlanStore(root).Update(ExpensePlans.ReversePayment(plan, row.Id), plan.UpdatedAt));
                    if (error is not null) Notify(error, error: true);
                }
            };
            var line = new StackPanel { Spacing = 4, Children = { text, action }, Margin = new Thickness(0, 8) };
            action.HorizontalAlignment = HorizontalAlignment.Left;
            return line;
        }, () => new TextBlock { Text = "La date prévue ne confirme pas le paiement.", Classes = { "caption" } }, plan.Id);
        body.Children.Add(new Expander { Header = $"Voir les échéances ({rows.Count})", Content = terms, Classes = { "estimate-section", "estimate-price-details", "expense-installments" }, HorizontalAlignment = HorizontalAlignment.Stretch });
        var card = new Expander { Header = heading, Content = body, IsExpanded = _expandedExpensePlan == plan.Id, Classes = { "estimate-section", "expense-plan-card" }, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 14) };
        card.Expanded += (_, _) => { _expandedExpensePlan = plan.Id; foreach (var other in PurchaseList.GetLogicalDescendants().OfType<Expander>().Where(e => e.Classes.Contains("expense-plan-card") && e != card)) other.IsExpanded = false; };
        card.Collapsed += (_, _) => { if (_expandedExpensePlan == plan.Id) _expandedExpensePlan = null; };
        return card;
    }

    private bool ExpenseContextCurrent(string root, string? navigation) => IsVisible && PurchasesWorkspacePanel.IsVisible && _purchaseScheduleView && MatchesNavigationContext(root, navigation);
    private async Task<string?> MutateExpensePlanAsync(string root, string? navigation, Func<ExpensePlan> write)
    {
        if (!ExpenseContextCurrent(root, navigation)) return "L’atelier ou la page a changé. Revenez à l’échéancier.";
        if (_expenseMutationBusy) return "Un enregistrement est déjà en cours.";
        _expenseMutationBusy = true;
        try
        {
            var plan = await Task.Run(write);
            if (ExpenseContextCurrent(root, navigation)) { _expandedExpensePlan = plan.Id; await LoadExpensePlansAsync(); }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return e.Message + " Actualisez la liste avant de réessayer. Votre formulaire est conservé."; }
        finally { _expenseMutationBusy = false; }
    }
}
