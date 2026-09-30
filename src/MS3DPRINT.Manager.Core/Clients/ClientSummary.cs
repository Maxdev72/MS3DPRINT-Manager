namespace MS3DPRINT.Manager.Core.Clients;

public sealed record ClientSummary(
    string ClientPath,
    string FolderName,
    string DisplayName,
    string ClientCode,
    ClientKind? Kind,
    ClientProfile? Profile,
    int ProjectCount)
{
    public bool IsProfileMissing => Profile is null;
}
