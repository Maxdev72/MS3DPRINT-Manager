using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class StorageProviderCard
{
    public StorageProviderCard(StorageProviderDefinition definition)
    {
        Kind = definition.Kind;
        DisplayName = definition.DisplayName;
        RootPath = definition.RootPath;
        IsAvailable = definition.IsAvailable;
        Status = definition.IsAvailable ? "Actif" : "Bientôt disponible";
    }

    public StorageProviderKind Kind { get; }
    public string DisplayName { get; }
    public string? RootPath { get; }
    public bool IsAvailable { get; }
    public string Status { get; }
}
