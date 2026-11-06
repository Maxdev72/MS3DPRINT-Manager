using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Server.Workspace;

public sealed class CommandStore
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private readonly object _gate = new();
    private readonly string _path;
    private bool _blockedUntilRecovery;

    public CommandStore(ServerStorage storage)
    {
        _path = Path.Combine(storage.StateRoot, "commands.sqlite");
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS commands (
                command_id TEXT PRIMARY KEY, request_hash TEXT NOT NULL,
                state TEXT NOT NULL CHECK(state IN ('Pending','Completed')),
                status_code INTEGER, content TEXT);
            """;
        command.ExecuteNonQuery();
    }

    public T Read<T>(Func<T> read)
    {
        lock (_gate)
        {
            if (_blockedUntilRecovery) throw new WorkspaceUnavailableException();
            return read();
        }
    }

    internal void BlockUntilRecovery() { lock (_gate) _blockedUntilRecovery = true; }

    public bool HasPending() => Read(() =>
    {
        using var connection = Open();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT EXISTS(SELECT 1 FROM commands WHERE state='Pending')";
        return (long)query.ExecuteScalar()! != 0;
    });

    public CommandReceiptResponse? Receipt(Guid id) => Read(() =>
    {
        var stored = Find(id);
        return stored is null ? null : new CommandReceiptResponse(id,
            stored.State == "Completed" ? "Completed" : "Uncertain", stored.StatusCode,
            stored.Content is null ? null : JsonSerializer.Deserialize<JsonElement>(stored.Content));
    });

    public IResult Execute<T>(Guid id, HttpRequest request, T input, Func<CommandResult> operation)
    {
        if (id == Guid.Empty) return Results.BadRequest(new { error = "L’identifiant de commande est requis." });
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.Method + "\n" + request.Path.Value + "\n" + JsonSerializer.Serialize(input, Json))));
        lock (_gate)
        {
            if (_blockedUntilRecovery) return Results.StatusCode(503);
            var stored = Find(id);
            if (stored is not null)
            {
                if (stored.Hash != hash || stored.State != "Completed")
                    return Results.Conflict(new { error = stored.State == "Completed"
                        ? "Cet identifiant désigne une autre commande." : "Résultat incertain. Vérifiez les données avant toute nouvelle action." });
                return Results.Content(stored.Content!, "application/json", statusCode: stored.StatusCode);
            }
            InsertPending(id, hash);
            CommandResult result;
            try { result = operation(); }
            catch (ProfileConflictException) { result = new(409, new { error = "La fiche a changé. Actualisez avant de réappliquer votre brouillon." }); }
            catch (KeyNotFoundException) { result = new(404, new { error = "La ressource est introuvable." }); }
            catch (ArgumentException) { result = new(400, new { error = "Les champs de la commande sont invalides." }); }
            catch (FolderConflictException) { result = new(409, new { error = "Un dossier existe déjà pour ce nom." }); }
            // A failed multi-file operation may have side effects: leave its durable Pending receipt intact.
            var content = JsonSerializer.Serialize(result.Content, result.Content.GetType(), Json);
            Complete(id, result.StatusCode, content);
            return Results.Content(content, "application/json", statusCode: result.StatusCode);
        }
    }

    private StoredCommand? Find(Guid id)
    {
        using var connection = Open();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT request_hash,state,status_code,content FROM commands WHERE command_id=$id";
        query.Parameters.AddWithValue("$id", id.ToString());
        using var row = query.ExecuteReader();
        return !row.Read() ? null : new(row.GetString(0), row.GetString(1), row.IsDBNull(2) ? null : row.GetInt32(2), row.IsDBNull(3) ? null : row.GetString(3));
    }

    private void InsertPending(Guid id, string hash)
    {
        using var connection = Open();
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO commands(command_id,request_hash,state) VALUES($id,$hash,'Pending')";
        insert.Parameters.AddWithValue("$id", id.ToString());
        insert.Parameters.AddWithValue("$hash", hash);
        insert.ExecuteNonQuery();
    }

    private void Complete(Guid id, int statusCode, string content)
    {
        using var connection = Open();
        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE commands SET state='Completed',status_code=$status,content=$content WHERE command_id=$id";
        update.Parameters.AddWithValue("$id", id.ToString());
        update.Parameters.AddWithValue("$status", statusCode);
        update.Parameters.AddWithValue("$content", content);
        update.ExecuteNonQuery();
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

    private sealed record StoredCommand(string Hash, string State, int? StatusCode, string? Content);
}

public sealed record CommandResult(int StatusCode, object Content);
public sealed class WorkspaceUnavailableException : InvalidOperationException
{
    public WorkspaceUnavailableException() : base("L’atelier attend une récupération vérifiée avant toute opération métier.") { }
}
