using Microsoft.Data.Sqlite;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Server.Workspace;

/// <summary>
/// Durable identity of each authenticated account and its workspace, kept in the private state folder.
/// The account is resolved from the session, never from a request parameter.
/// </summary>
public sealed class WorkspaceRegistry
{
    public const string DefaultName = "Atelier";
    private readonly string _path;

    public WorkspaceRegistry(ServerStorage storage)
    {
        _path = Path.Combine(storage.StateRoot, "workspace.sqlite");
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS workspaces (
                account_subject TEXT PRIMARY KEY, account_id TEXT NOT NULL UNIQUE,
                workspace_id TEXT NOT NULL UNIQUE, name TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }

    public RegisteredWorkspace For(string accountSubject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountSubject);
        using var connection = Open();
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT OR IGNORE INTO workspaces VALUES($subject,$account,$workspace,$name)";
            insert.Parameters.AddWithValue("$subject", accountSubject);
            insert.Parameters.AddWithValue("$account", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$workspace", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("$name", DefaultName);
            insert.ExecuteNonQuery();
        }
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT account_id,workspace_id,name FROM workspaces WHERE account_subject=$subject";
        query.Parameters.AddWithValue("$subject", accountSubject);
        using var row = query.ExecuteReader();
        row.Read();
        return new(row.GetString(0), Guid.Parse(row.GetString(1)), row.GetString(2));
    }

    private SqliteConnection Open()
    {
        WorkspacePathSafety.EnsureNoLinks(_path);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        try
        {
            connection.Open();
            using var durable = connection.CreateCommand();
            durable.CommandText = "PRAGMA synchronous=FULL";
            durable.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
}

public sealed record RegisteredWorkspace(string AccountId, Guid WorkspaceId, string Name);
