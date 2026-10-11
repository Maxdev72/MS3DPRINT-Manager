using MS3DPRINT.Manager.Core.Collections;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

public sealed class WorkspaceCollectionEditor
{
    public CollectionProfile Save(string workspaceRoot, CollectionItemSummary item, string category, string name, string? description, string? notes,
        string? contact = null, string? address = null, string? phone = null, string? email = null, string? website = null)
    {
        var store = new CollectionProfileStore(workspaceRoot);
        var existing = item.Profile ?? store.FindByPath(item.Path);
        var now = DateTimeOffset.UtcNow;
        var profile = existing ?? new CollectionProfile(Guid.NewGuid(), category,
            Path.GetRelativePath(Path.GetFullPath(workspaceRoot), Path.GetFullPath(item.Path)).Replace('\\', '/'), item.Name,
            null, null, null, null, null, null, null, now, now);
        profile = profile with
        {
            Name = name.Trim(), Description = Normalize(description), Notes = Normalize(notes),
            Contact = Normalize(contact), Address = Normalize(address), Phone = Normalize(phone),
            Email = Normalize(email), Website = Normalize(website), UpdatedAt = now
        };
        return existing is null ? store.Create(profile) : store.Update(profile, existing.UpdatedAt);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
