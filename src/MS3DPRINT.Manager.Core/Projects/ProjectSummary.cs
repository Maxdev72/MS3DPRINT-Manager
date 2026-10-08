namespace MS3DPRINT.Manager.Core.Projects;

public sealed record ProjectSummary(string ClientFolderName, string ClientPath, string ProjectPath, string Reference, string FolderName, ProjectProfile? Profile)
{
    public bool IsProfileMissing => Profile is null;
    public ProjectStatus? Status => Profile?.Status;
    public string StatusLabel => Status switch
    {
        ProjectStatus.Quote => "DEVIS",
        ProjectStatus.InProgress => "EN_COURS",
        ProjectStatus.Completed => "TERMINE",
        _ => "À compléter"
    };
    public string ProjectName => Profile?.ProjectName ?? (FolderName.StartsWith(Reference + "_", StringComparison.Ordinal) ? FolderName[(Reference.Length + 1)..] : FolderName);
}
