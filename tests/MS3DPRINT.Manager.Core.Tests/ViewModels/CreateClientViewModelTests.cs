using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Core.Tests.ViewModels;

public sealed class CreateClientViewModelTests
{
    [Fact]
    public async Task Lookup_TimeoutExplainsManualFallback()
    {
        using var model = new CreateClientViewModel(new TimeoutLookup());
        model.CompanyQuery = "Atelier";
        await WaitUntilAsync(() => model.LookupStatus.Contains("saisie manuelle"));
        Assert.Contains("saisie manuelle", model.LookupStatus);
    }

    private sealed class TimeoutLookup : MS3DPRINT.Manager.App.Clients.IClientLookup
    {
        public Task<IReadOnlyList<MS3DPRINT.Manager.App.Clients.CompanySuggestion>> SearchCompaniesAsync(string query, CancellationToken cancellationToken) => throw new TaskCanceledException("Timeout");
        public Task<IReadOnlyList<MS3DPRINT.Manager.App.Clients.AddressSuggestion>> SearchAddressesAsync(string query, CancellationToken cancellationToken) => throw new TaskCanceledException("Timeout");
    }
    [Fact]
    public async Task Lookup_DebouncesRapidEditsAndRejectsLateResultAfterTypeSwitch()
    {
        var lookup = new DelayedLookup();
        using var model = new CreateClientViewModel(lookup);
        model.CompanyQuery = "old";
        model.CompanyQuery = "current";
        await lookup.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("current", lookup.Query);
        model.Kind = ClientKind.Individual;
        Assert.True(lookup.Token.IsCancellationRequested);
        lookup.Completion.SetResult([new("Stale company", "12345678900012", "Address")]);
        await Task.Delay(50);
        Assert.Empty(model.CompanySuggestions);
    }

    [Fact]
    public void SelectingCompany_PopulatesEditableFieldsAndIndividualProfileOmitsSiret()
    {
        using var model = new CreateClientViewModel(new OfflineLookup());
        model.ApplyCompanySuggestion(new("Atelier", "12345678900012", "1 rue Test"));
        Assert.Equal("Atelier", model.ClientName);
        Assert.Equal("12345678900012", model.CreateProfile().Siret);
        Assert.Equal("1 rue Test", model.Address);
        model.Siret = "98765432100012";
        Assert.Equal("98765432100012", model.CreateProfile().Siret);
        model.Kind = ClientKind.Individual;
        Assert.Null(model.CreateProfile().Siret);
    }

    private sealed class DelayedLookup : MS3DPRINT.Manager.App.Clients.IClientLookup
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<MS3DPRINT.Manager.App.Clients.CompanySuggestion>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Query;
        public CancellationToken Token;
        public Task<IReadOnlyList<MS3DPRINT.Manager.App.Clients.CompanySuggestion>> SearchCompaniesAsync(string query, CancellationToken cancellationToken)
        {
            Query = query; Token = cancellationToken; Started.SetResult(); return Completion.Task;
        }
        public Task<IReadOnlyList<MS3DPRINT.Manager.App.Clients.AddressSuggestion>> SearchAddressesAsync(string query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MS3DPRINT.Manager.App.Clients.AddressSuggestion>>([]);
    }
    [Fact]
    public void Individual_OmitsProfessionalContactIdentityButKeepsCommunication()
    {
        using var model = new CreateClientViewModel(new OfflineLookup()) { Kind = ClientKind.Individual, FirstName = "Alice", LastName = "Martin", ContactFirstName = "Other", ContactLastName = "Person", ContactRole = "Director", ContactPhone = "0123456789", ContactEmail = "alice@example.test" };
        var profile = model.CreateProfile();
        Assert.Null(profile.CompanyName);
        Assert.Null(profile.PrimaryContact.FirstName);
        Assert.Null(profile.PrimaryContact.LastName);
        Assert.Null(profile.PrimaryContact.Role);
        Assert.Equal("0123456789", profile.PrimaryContact.Phone);
        Assert.Equal("alice@example.test", profile.PrimaryContact.Email);
    }

    [Fact]
    public void Profile_HasOptionalBackwardCompatibleSiret()
    {
        using var model = new CreateClientViewModel(new OfflineLookup()) { ClientName = "Atelier" };
        var profile = model.CreateProfile();
        var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(profile))!;
        node.AsObject().Remove("Siret");
        var json = node.ToJsonString();
        var legacy = System.Text.Json.JsonSerializer.Deserialize<ClientProfile>(json)!;
        var property = typeof(ClientProfile).GetProperty("Siret");
        Assert.NotNull(property);
        Assert.Null(property.GetValue(legacy));
    }

    [Fact]
    public async Task Suggestions_IndividualDoesNotSearchCompanyAndOfflineKeepsManualFields()
    {
        var lookup = new OfflineLookup();
        var constructor = typeof(CreateClientViewModel).GetConstructor([typeof(MS3DPRINT.Manager.App.Clients.IClientLookup)]);
        Assert.NotNull(constructor);
        using var model = (CreateClientViewModel)constructor.Invoke([lookup]);
        model.Kind = ClientKind.Individual;
        model.ClientName = "Manual company";
        Assert.Equal(0, lookup.CompanyCalls);
        model.Kind = ClientKind.Professional;
        model.ClientName = "Atelier manuel";
        await WaitUntilAsync(() => !string.IsNullOrEmpty(model.LookupStatus));
        Assert.Equal("Atelier manuel", model.ClientName);
        Assert.Equal(1, lookup.CompanyCalls);
    }

    private sealed class OfflineLookup : MS3DPRINT.Manager.App.Clients.IClientLookup
    {
        public int CompanyCalls;
        public Task<IReadOnlyList<MS3DPRINT.Manager.App.Clients.CompanySuggestion>> SearchCompaniesAsync(string query, CancellationToken cancellationToken)
        {
            CompanyCalls++;
            throw new System.Net.Http.HttpRequestException("Offline");
        }
        public Task<IReadOnlyList<MS3DPRINT.Manager.App.Clients.AddressSuggestion>> SearchAddressesAsync(string query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MS3DPRINT.Manager.App.Clients.AddressSuggestion>>([]);
    }
    [Fact]
    public void Individual_UsesFirstAndLastNameForTheFolderPreview()
    {
        using var viewModel = new CreateClientViewModel(new OfflineLookup())
        {
            Kind = ClientKind.Individual,
            FirstName = "Élodie",
            LastName = "Du Pont"
        };

        Assert.Equal("ELODIE_DU_PONT", viewModel.NormalizedName);
    }

    [Fact]
    public void CreateProfile_CopiesAProfessionalPrimaryContact()
    {
        using var viewModel = new CreateClientViewModel(new OfflineLookup())
        {
            ClientName = "MPO",
            ClientCode = "MPO",
            ContactFirstName = "Marie",
            ContactLastName = "Durand",
            ContactEmail = "marie@example.test",
            Address = "1 rue de l'Atelier",
            Notes = "Contact par e-mail"
        };

        var profile = viewModel.CreateProfile();

        Assert.Equal("MPO", profile.CompanyName);
        Assert.Equal("Marie", profile.PrimaryContact.FirstName);
        Assert.Equal("Contact par e-mail", profile.Notes);
    }

    [Fact]
    public void ExistingFolder_KeepsItsFolderAndCodeWhenCreatingTheProfile()
    {
        using var viewModel = new CreateClientViewModel("MPO", "MPO") { Kind = ClientKind.Individual, ClientName = "MPO Industrie" };
        viewModel.Kind = ClientKind.Professional;

        var profile = viewModel.CreateProfile();

        Assert.Equal("MPO", profile.FolderName);
        Assert.Equal("MPO", profile.ClientCode);
        Assert.Equal("MPO Industrie", profile.CompanyName);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}
