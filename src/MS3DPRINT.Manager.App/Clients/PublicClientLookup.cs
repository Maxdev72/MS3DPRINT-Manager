using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS3DPRINT.Manager.App.Clients;

public sealed record CompanySuggestion(string Name, string? Siret, string? Address)
{
    public string Label => $"{Name} — {Siret} — {Address}";
}

public sealed record AddressSuggestion(string Label);

public interface IClientLookup
{
    Task<IReadOnlyList<CompanySuggestion>> SearchCompaniesAsync(string query, CancellationToken cancellationToken);
    Task<IReadOnlyList<AddressSuggestion>> SearchAddressesAsync(string query, CancellationToken cancellationToken);
}

public sealed class PublicClientLookup(HttpClient httpClient) : IClientLookup
{
    public async Task<IReadOnlyList<CompanySuggestion>> SearchCompaniesAsync(string query, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"https://recherche-entreprises.api.gouv.fr/search?q={Uri.EscapeDataString(query.Trim())}&per_page=5", cancellationToken);
        response.EnsureSuccessStatusCode();
        var data = await JsonSerializer.DeserializeAsync<CompanyResponse>(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return data?.Results?.Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item =>
            {
                var establishment = item.MatchingEstablishments?.FirstOrDefault(site => site.Siret == query.Trim()) ?? item.HeadOffice;
                return new CompanySuggestion(item.Name!, establishment?.Siret, establishment?.Address);
            }).ToArray() ?? [];
    }

    public async Task<IReadOnlyList<AddressSuggestion>> SearchAddressesAsync(string query, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"https://data.geopf.fr/geocodage/completion/?text={Uri.EscapeDataString(query.Trim())}&type=StreetAddress&maximumResponses=5", cancellationToken);
        response.EnsureSuccessStatusCode();
        var data = await JsonSerializer.DeserializeAsync<AddressResponse>(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return data?.Results?.Where(item => !string.IsNullOrWhiteSpace(item.Label)).Select(item => new AddressSuggestion(item.Label!)).ToArray() ?? [];
    }

    private sealed record CompanyResponse([property: JsonPropertyName("results")] CompanyResult[]? Results);
    private sealed record CompanyResult([property: JsonPropertyName("nom_complet")] string? Name, [property: JsonPropertyName("siege")] HeadOffice? HeadOffice, [property: JsonPropertyName("matching_etablissements")] HeadOffice[]? MatchingEstablishments);
    private sealed record HeadOffice([property: JsonPropertyName("siret")] string? Siret, [property: JsonPropertyName("adresse")] string? Address);
    private sealed record AddressResponse([property: JsonPropertyName("results")] AddressResult[]? Results);
    private sealed record AddressResult([property: JsonPropertyName("fulltext")] string? Label);
}
