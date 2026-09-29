namespace MS3DPRINT.Manager.Core.Storage;

public sealed class FolderConflictException : IOException
{
    public FolderConflictException(string message) : base(message) { }

    public FolderConflictException(string message, Exception innerException)
        : base(message, innerException) { }
}
