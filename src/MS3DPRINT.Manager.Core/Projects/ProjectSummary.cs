namespace MS3DPRINT.Manager.Core.Projects;

public sealed record ProjectSummary(string ClientFolderName, string ClientPath, string ProjectPath, string Reference, string FolderName, ProjectProfile? Profile)
{
    public bool IsProfileMissing => Profile is null;
    public ProjectStatus? Status => Profile?.Status;

    /// <summary>Three states only: A_FAIRE (also a project whose sheet is still missing), EN_COURS and TERMINE.</summary>
    public string StatusLabel => Status switch
    {
        ProjectStatus.InProgress => "EN_COURS",
        ProjectStatus.Completed => "TERMINE",
        _ => "A_FAIRE"
    };

    /// <summary>Where the project stands, from its status: nothing done, half way, finished.</summary>
    public int ProgressPercent => Status switch
    {
        ProjectStatus.InProgress => 50,
        ProjectStatus.Completed => 100,
        _ => 0
    };

    public string ProjectName => Profile?.ProjectName ?? (FolderName.StartsWith(Reference + "_", StringComparison.Ordinal) ? FolderName[(Reference.Length + 1)..] : FolderName);
}
