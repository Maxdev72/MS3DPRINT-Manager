namespace MS3DPRINT.Manager.Core.Workspace;

public sealed record TrashEntry(Guid Id, string Label, DateTimeOffset DeletedAt)
{
    public DateTimeOffset LocalDeletedAt => DeletedAt.ToLocalTime();
}
