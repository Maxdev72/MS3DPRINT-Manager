namespace MS3DPRINT.Manager.Core.Remote;

/// <summary>A native device token. Persist only in the operating system credential vault.</summary>
public sealed class RemoteDeviceSession
{
    /// <summary>Server grants 30 days to native apps; one extra day absorbs clock drift.</summary>
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromDays(31);

    public string Server { get; }
    public string Account { get; }
    public string AccessToken { get; }
    public DateTimeOffset ExpiresAt { get; }

    public RemoteDeviceSession(string server, string account, string accessToken, DateTimeOffset expiresAt)
    {
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.AbsolutePath != "/"
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Adresse HTTPS racine requise.", nameof(server));
        if (string.IsNullOrWhiteSpace(account) || account.Length > 100 || account.Any(char.IsControl))
            throw new ArgumentException("Compte invalide.", nameof(account));
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 4096 || accessToken.Any(char.IsControl))
            throw new ArgumentException("Session invalide.", nameof(accessToken));
        Server = uri.AbsoluteUri; Account = account; AccessToken = accessToken; ExpiresAt = expiresAt;
    }

    public override string ToString() => $"{Account} — {Server} ({ExpiresAt:O})";
}

public interface IRemoteSessionStore
{
    RemoteDeviceSession? Load();
    void Save(RemoteDeviceSession session);
    void Clear();
}
