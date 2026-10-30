using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Desktop;
using MS3DPRINT.Manager.Desktop.Clients;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Workshop_identity_suggestions_fill_company_and_address_without_changing_contacts() => Run(async () =>
    {
        var lookup = new IdentityLookup();
        var current = new RevenueHeader("Mon atelier", "", "Camille", "Adresse existante", "atelier@exemple.fr", "0601020304");
        var owner = await Open(); owner.Show();
        var dialog = new RevenueHeaderWindow(current, lookup); var shown = dialog.ShowDialog<bool>(owner);
        try
        {
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var body = owner.FindControl<Border>("SidePanelBody")!;
            var companies = IdentityControl<ListBox>(body, "Entreprises trouvées");
            var addresses = IdentityControl<ListBox>(body, "Adresses trouvées");
            await Task.Delay(450); Assert.Empty(lookup.CompanyQueries); Assert.Empty(lookup.AddressQueries);
            Assert.False(companies.IsVisible);
            var business = IdentityControl<TextBox>(body, "Entreprise");
            business.Text = "12345678900012";
            await WaitForIdentity(() => companies.IsVisible);
            Assert.Equal("12345678900012", Assert.Single(lookup.CompanyQueries));
            foreach (var width in new[] { 960d, 1400d })
            { owner.Width = width; await Task.Delay(180); CaptureDraftUi(owner, "workshop-company-suggestions-" + width); }
            companies.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            Assert.Equal("ATELIER TEST", dialog.Result.BusinessName);
            Assert.Equal("12345678900012", dialog.Result.Siret);
            Assert.Equal("1 RUE TEST 75001 PARIS", dialog.Result.Address);
            Assert.Equal(current.OwnerName, dialog.Result.OwnerName);
            Assert.Equal(current.Email, dialog.Result.Email); Assert.Equal(current.Phone, dialog.Result.Phone);
            await Task.Delay(450); Assert.False(companies.IsVisible); Assert.Empty(lookup.AddressQueries);
            var address = IdentityControl<TextBox>(body, "Adresse"); address.Text = "2 rue de Paris";
            await WaitForIdentity(() => addresses.IsVisible);
            addresses.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            Assert.Equal("2 Rue de Paris, 80000 Amiens", dialog.Result.Address);
            await Task.Delay(450); Assert.False(addresses.IsVisible); Assert.Single(lookup.AddressQueries);
        }
        finally { dialog.Close(false); await shown; owner.Close(); }
    });

    [Fact]
    public Task Workshop_identity_ignores_late_results_and_stops_searching_when_closed() => Run(async () =>
    {
        var first = new TaskCompletionSource<IReadOnlyList<CompanySuggestion>>();
        var last = new TaskCompletionSource<IReadOnlyList<CompanySuggestion>>();
        var lookup = new IdentityLookup { Companies = (query, _) => query == "Ancienne" ? first.Task
            : query == "Fermeture" ? last.Task : Task.FromResult<IReadOnlyList<CompanySuggestion>>([new("Nouvelle entreprise", "222", "Paris")]) };
        var dialog = new RevenueHeaderWindow(new(), lookup); dialog.Show();
        try
        {
            var input = IdentityControl<TextBox>(dialog, "Entreprise");
            var companies = IdentityControl<ListBox>(dialog, "Entreprises trouvées");
            input.Text = "Ancienne"; await WaitForIdentity(() => lookup.CompanyQueries.Count == 1);
            input.Text = "Nouvelle"; await WaitForIdentity(() => companies.IsVisible);
            first.SetResult([new("Ancienne entreprise", "111", "Lyon")]);
            await Task.Delay(60); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Nouvelle entreprise", Assert.Single(companies.ItemsSource!.Cast<CompanySuggestion>()).Name);
            input.Text = "Fermeture"; await WaitForIdentity(() => lookup.CompanyQueries.Count == 3);
            dialog.Close(false); last.SetResult([new("Après fermeture", null, null)]);
            await Task.Delay(60); Dispatcher.UIThread.RunJobs(); Assert.False(companies.IsVisible);
        }
        finally { dialog.Close(false); }
    });

    [Fact]
    public Task Workshop_identity_keeps_manual_entry_available_when_lookup_fails() => Run(async () =>
    {
        var lookup = new IdentityLookup { Companies = (_, _) => throw new HttpRequestException("Hors ligne") };
        var dialog = new RevenueHeaderWindow(new(), lookup); dialog.Show();
        try
        {
            var input = IdentityControl<TextBox>(dialog, "Entreprise"); input.Text = "Mon atelier manuel";
            await WaitForIdentity(() => dialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsVisible && t.Text?.Contains("indisponibles") == true));
            Assert.Equal("Mon atelier manuel", dialog.Result.BusinessName);
            Assert.True(input.IsEnabled);
            Assert.False(IdentityControl<ListBox>(dialog, "Entreprises trouvées").IsVisible);
        }
        finally { dialog.Close(false); }
    });

    private static T IdentityControl<T>(Control root, string name) where T : Control
        => Assert.Single(root.GetVisualDescendants().OfType<T>().Where(c => AutomationProperties.GetName(c) == name));

    private static async Task WaitForIdentity(Func<bool> ready)
    {
        for (var index = 0; index < 150 && !ready(); index++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
        Assert.True(ready());
    }

    private sealed class IdentityLookup : IClientLookup
    {
        public List<string> CompanyQueries { get; } = [];
        public List<string> AddressQueries { get; } = [];
        public Func<string, CancellationToken, Task<IReadOnlyList<CompanySuggestion>>> Companies { get; init; }
            = (_, _) => Task.FromResult<IReadOnlyList<CompanySuggestion>>([new("ATELIER TEST", "12345678900012", "1 RUE TEST 75001 PARIS")]);
        public Task<IReadOnlyList<CompanySuggestion>> SearchCompaniesAsync(string query, CancellationToken token)
        { CompanyQueries.Add(query); return Companies(query, token); }
        public Task<IReadOnlyList<AddressSuggestion>> SearchAddressesAsync(string query, CancellationToken token)
        { AddressQueries.Add(query); return Task.FromResult<IReadOnlyList<AddressSuggestion>>([new("2 Rue de Paris, 80000 Amiens")]); }
    }
}
