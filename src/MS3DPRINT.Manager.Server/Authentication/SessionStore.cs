using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Server.Authentication;

public sealed class SessionStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
    private static readonly PasswordHasher<string> Hasher = new();
    private static readonly string DummyHash = Hasher.HashPassword("owner", Guid.NewGuid().ToString());
    private readonly string _path;
    private readonly TimeProvider _clock;

    public SessionStore(ServerStorage storage, TimeProvider clock)
    {
        _path = Path.Combine(storage.StateRoot, "identity.sqlite");
        _clock = clock;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS owner (id INTEGER PRIMARY KEY CHECK(id=1), name TEXT NOT NULL, password_hash TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sessions (token_hash TEXT PRIMARY KEY, expires_at INTEGER NOT NULL);
            """;
        command.ExecuteNonQuery();
    }

    public void CreateOwner(string name, string password)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.Any(char.IsControl))
            throw new ArgumentException("Le nom du propriétaire est requis (100 caractères maximum).");
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 16 or > 1024)
            throw new ArgumentException("Utilisez un mot de passe de 16 à 1024 caractères.");
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO owner SELECT 1, $name, $hash WHERE NOT EXISTS (SELECT 1 FROM owner)";
        command.Parameters.AddWithValue("$name", name.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$hash", Hasher.HashPassword("owner", password));
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Le propriétaire existe déjà ; aucun compte n’a été remplacé.");
    }

    public bool VerifyOwner(string name, string password)
    {
        if (name.Length > 100 || password.Length > 1024) return false;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, password_hash FROM owner WHERE id=1";
        using var reader = command.ExecuteReader();
        var exists = reader.Read();
        var storedName = exists ? reader.GetString(0) : "";
        var hash = exists ? reader.GetString(1) : DummyHash;
        var valid = Hasher.VerifyHashedPassword("owner", hash, password) != PasswordVerificationResult.Failed;
        return exists && valid && storedName == name.Trim().ToUpperInvariant();
    }

    public string CreateSession()
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM sessions WHERE expires_at <= $now";
        command.Parameters.AddWithValue("$now", _clock.GetUtcNow().ToUnixTimeSeconds());
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO sessions SELECT $hash, $expires WHERE EXISTS (SELECT 1 FROM owner WHERE id=1)";
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$hash", Hash(token));
        command.Parameters.AddWithValue("$expires", (_clock.GetUtcNow() + Lifetime).ToUnixTimeSeconds());
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Créez le propriétaire avant de vous connecter.");
        transaction.Commit();
        return token;
    }

    public bool IsActive(string token)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) return false;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sessions WHERE token_hash=$hash AND expires_at > $now";
        command.Parameters.AddWithValue("$hash", Hash(token));
        command.Parameters.AddWithValue("$now", _clock.GetUtcNow().ToUnixTimeSeconds());
        return (long)command.ExecuteScalar()! == 1;
    }

    public void Revoke(string token)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sessions WHERE token_hash=$hash";
        command.Parameters.AddWithValue("$hash", Hash(token));
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        WorkspacePathSafety.EnsureNoLinks(_path);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
