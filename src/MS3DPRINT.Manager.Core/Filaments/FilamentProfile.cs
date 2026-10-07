namespace MS3DPRINT.Manager.Core.Filaments;

public sealed record FilamentProfile(Guid Id, string Brand, string Name, string Material,
    decimal PricePerKg, bool IsAbrasive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
