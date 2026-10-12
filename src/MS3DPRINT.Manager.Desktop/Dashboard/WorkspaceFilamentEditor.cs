using MS3DPRINT.Manager.Core.Filaments;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

public sealed class WorkspaceFilamentEditor
{
    public FilamentProfile Create(string workspaceRoot, string brand, string name, string material, decimal pricePerKg, bool isAbrasive, string? color = null)
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new FilamentProfile(Guid.NewGuid(), brand.Trim(), name.Trim(), material.Trim(), pricePerKg, isAbrasive, now, now, color?.Trim());
        new FilamentStore(workspaceRoot).Create(profile);
        return profile;
    }

    public FilamentProfile Update(string workspaceRoot, FilamentProfile original, string brand, string name, string material, decimal pricePerKg, bool isAbrasive, string? color = null)
        => new FilamentStore(workspaceRoot).Update(original with { Brand = brand.Trim(), Name = name.Trim(), Material = material.Trim(), PricePerKg = pricePerKg, IsAbrasive = isAbrasive, Color = color?.Trim() }, original.UpdatedAt);
}
