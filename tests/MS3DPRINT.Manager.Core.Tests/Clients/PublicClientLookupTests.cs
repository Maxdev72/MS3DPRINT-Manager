using System.Net;
using System.Reflection;
using MS3DPRINT.Manager.App.ViewModels;

namespace MS3DPRINT.Manager.Core.Tests.Clients;

public sealed class PublicClientLookupTests
{
    [Fact]
    public async Task SiretSearch_UsesMatchedEstablishmentInsteadOfHeadOffice()
    {
        var handler = new FixtureHandler("""{"results":[{"nom_complet":"ATELIER","siege":{"siret":"12345678900012","adresse":"SIEGE"},"matching_etablissements":[{"siret":"12345678900020","adresse":"ATELIER LOCAL"}]}]}""");
        var service = new MS3DPRINT.Manager.App.Clients.PublicClientLookup(new HttpClient(handler));
        var item = Assert.Single(await service.SearchCompaniesAsync("12345678900020", CancellationToken.None));
        Assert.Equal("12345678900020", item.Siret);
        Assert.Equal("ATELIER LOCAL", item.Address);
    }

    [Fact]
    public async Task AddressSearch_ParsesIgnFulltextAndUsesCurrentEndpoint()
    {
        var handler = new FixtureHandler("""{"status":"OK","results":[{"x":2.29,"y":49.88,"country":"StreetAddress","city":"Amiens","oldcity":"","kind":"housenumber","zipcode":"80000","street":"Rue de Paris","metropole":true,"fulltext":"1 Rue de Paris, 80000 Amiens","classification":7}]}""");
        var service = new MS3DPRINT.Manager.App.Clients.PublicClientLookup(new HttpClient(handler));
        Assert.Equal("1 Rue de Paris, 80000 Amiens", Assert.Single(await service.SearchAddressesAsync("1 rue de Paris", CancellationToken.None)).Label);
        Assert.Equal("data.geopf.fr", handler.Uri!.Host);
    }
    [Fact]
    public async Task CompanyLookup_RetainsNameSiretAndAddress()
    {
        var type = typeof(CreateClientViewModel).Assembly.GetType("MS3DPRINT.Manager.App.Clients.PublicClientLookup");
        Assert.NotNull(type);
        var handler = new FixtureHandler("""{"results":[{"nom_complet":"ATELIER TEST","siege":{"siret":"12345678900012","adresse":"1 RUE TEST 75001 PARIS"}}]}""");
        var service = Activator.CreateInstance(type, new HttpClient(handler))!;
        var task = (Task)type.GetMethod("SearchCompaniesAsync")!.Invoke(service, ["atelier & test", CancellationToken.None])!;
        await task;
        var suggestions = (System.Collections.IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!;
        var item = suggestions.Cast<object>().Single();
        Assert.Equal("ATELIER TEST", item.GetType().GetProperty("Name")!.GetValue(item));
        Assert.Equal("12345678900012", item.GetType().GetProperty("Siret")!.GetValue(item));
        Assert.Equal("1 RUE TEST 75001 PARIS", item.GetType().GetProperty("Address")!.GetValue(item));
        Assert.Contains("atelier%20%26%20test", handler.Uri!.AbsoluteUri);
    }

    private sealed class FixtureHandler(string json) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
