namespace MS3DPRINT.Manager.Core.Storage;

public sealed record FolderCreationResult(string DestinationPath, IReadOnlyList<string> CreatedFolders);
