using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Files;

public sealed class FolderSizeService
{
    public Task<long?> GetSizeAsync(string folder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return Task.Run(() => GetSize(folder, cancellationToken), cancellationToken);
    }

    private static long? GetSize(string folder, CancellationToken cancellationToken)
    {
        try
        {
            WorkspacePathSafety.EnsureNoLinks(folder);
            long total = 0;
            var pending = new Stack<string>();
            pending.Push(folder);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = pending.Pop();
                foreach (var path in Directory.EnumerateFileSystemEntries(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        WorkspacePathSafety.EnsureNoLinks(path);
                        if (Directory.Exists(path)) pending.Push(path);
                        else total = checked(total + new FileInfo(path).Length);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        return null;
                    }
                }
            }
            return total;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
