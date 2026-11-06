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
            CREATE TABLE IF NOT EXISTS revision (id INTEGER PRIMARY KEY CHECK(id=1), value INTEGER NOT NULL);
            INSERT OR IGNORE INTO revision VALUES(1, 0);
            UPDATE revision SET value = value + 1;
            """;
        // Bumped at each start: a migration recovery or an offline change while stopped is never assumed already known.
        command.ExecuteNonQuery();
    }

    /// <summary>Revision of acknowledged changes; unverified while any command result is uncertain.</summary>
    public WorkspaceRevisionResponse Revision() => Read(() =>
    {
        using var connection = Open();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT value, NOT EXISTS(SELECT 1 FROM commands WHERE state='Pending') FROM revision WHERE id=1";
        using var row = query.ExecuteReader();
        row.Read();
        return new WorkspaceRevisionResponse(row.GetInt64(0), row.GetInt64(1) != 0);
    });

    public T Read<T>(Func<T> read)
    {
        lock (_gate)
        {
            if (_blockedUntilRecovery) throw new WorkspaceUnavailableException();
            return read();
        }
    }

    internal void BlockUntilRecovery() { lock (_gate) _blockedUntilRecovery = true; }

    internal const string UncertainMessage = "Une modification précédente a un résultat incertain. Les écritures sont suspendues jusqu’à sa vérification par l’administrateur.";

    public bool HasPending() => Read(AnyPending);

    private bool AnyPending()
    {
        using var connection = Open();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT EXISTS(SELECT 1 FROM commands WHERE state='Pending')";
        return (long)query.ExecuteScalar()! != 0;
    }

    /// <summary>
    /// Administrator action after checking the records by hand: closes one uncertain command as a recorded conflict,
    /// so its client refreshes instead of replaying it, and writes resume. Returns false when it was not Pending.
    /// </summary>
    public bool ResolveUncertain(Guid id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE commands SET state='Completed',status_code=409,content=$content WHERE command_id=$id AND state='Pending'";
            update.Parameters.AddWithValue("$id", id.ToString());
            update.Parameters.AddWithValue("$content", JsonSerializer.Serialize(new { error = "Résultat vérifié par l’administrateur. Actualisez avant toute nouvelle modification." }, Json));
            return update.ExecuteNonQuery() == 1;
        }
    }

    public IReadOnlyList<Guid> UncertainCommands()
    {
        using var connection = Open();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT command_id FROM commands WHERE state='Pending' ORDER BY command_id";
        using var row = query.ExecuteReader();
        var ids = new List<Guid>();
        while (row.Read()) ids.Add(Guid.Parse(row.GetString(0)));
        return ids;
    }

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
            // An uncertain result may have changed the records: no new write until an administrator has checked it.
            if (AnyPending()) return Results.Json(new { error = UncertainMessage }, Json, statusCode: 503);
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
        using var transaction = connection.BeginTransaction();
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE commands SET state='Completed',status_code=$status,content=$content WHERE command_id=$id;
            UPDATE revision SET value = value + 1 WHERE $status BETWEEN 200 AND 299;
            """;
        update.Parameters.AddWithValue("$id", id.ToString());
        update.Parameters.AddWithValue("$status", statusCode);
        update.Parameters.AddWithValue("$content", content);
        update.ExecuteNonQuery();
        transaction.Commit();
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
