using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class StorageProviderCard
{
    public StorageProviderCard(StorageProviderDefinition definition)
    {
        DisplayName = definition.DisplayName;
        RootPath = definition.RootPath;
        IsAvailable = definition.IsAvailable;
        Status = definition.IsAvailable ? "Actif" : "Bientôt disponible";
        Icon = definition.Kind switch
        {
            StorageProviderKind.Nextcloud => "☁",
            StorageProviderKind.GoogleDrive => "△",
            StorageProviderKind.Dropbox => "◇",
            _ => "▣"
        };
        IconColor = definition.Kind switch
        {
            StorageProviderKind.Nextcloud => "#0082C9",
            StorageProviderKind.GoogleDrive => "#4285F4",
            StorageProviderKind.Dropbox => "#0061FF",
            _ => "#64748B"
        };
    }

    public string DisplayName { get; }
    public string? RootPath { get; }
    public bool IsAvailable { get; }
    public string Status { get; }
    public string Icon { get; }
    public string IconColor { get; }
}
