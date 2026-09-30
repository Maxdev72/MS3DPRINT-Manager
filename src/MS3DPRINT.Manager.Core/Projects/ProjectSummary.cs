namespace MS3DPRINT.Manager.Core.Projects;

public sealed record ProjectSummary(string ClientFolderName, string ClientPath, string ProjectPath, string Reference, string FolderName, ProjectProfile? Profile)
{
    public bool IsProfileMissing => Profile is null;
    public ProjectStatus? Status => Profile?.Status;
    public string ProjectName => Profile?.ProjectName ?? FolderName[(FolderName.IndexOf('_') + 1)..];
}
