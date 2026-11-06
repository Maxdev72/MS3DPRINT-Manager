using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Linux;

/// <summary>Uses the desktop Secret Service; never writes tokens to a file or command line.</summary>
public sealed class LinuxRemoteSessionStore(string executable = "secret-tool", string key = "MS3DPRINT.Manager/RemoteDeviceSession") : IRemoteSessionStore
{
    public RemoteDeviceSession? Load()
    {
        var json = Run("lookup", null, allowMissing: true);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var value = JsonSerializer.Deserialize<Stored>(json) ?? throw new JsonException();
            return new RemoteDeviceSession(value.Server, value.Account, value.AccessToken, value.ExpiresAt);
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        { throw new InvalidDataException("La session du coffre Linux est illisible. Déconnectez cet appareil puis reconnectez-vous."); }
    }

    public void Save(RemoteDeviceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Run("store", JsonSerializer.Serialize(new Stored(session.Server, session.Account, session.AccessToken, session.ExpiresAt)));
    }

    public void Clear() => Run("clear", null, allowMissing: true);

    private string? Run(string action, string? input, bool allowMissing = false)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        start.ArgumentList.Add(action);
        if (action == "store") start.ArgumentList.Add("--label=MS3DPRINT Manager");
        start.ArgumentList.Add("application"); start.ArgumentList.Add(key);
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("Coffre Linux indisponible.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (input is not null) process.StandardInput.Write(input);
            process.StandardInput.Close();
            if (!process.WaitForExit(15000))
            {
                process.Kill(entireProcessTree: true);
                throw new IOException("Le coffre Linux ne répond pas. Déverrouillez le trousseau de votre session.");
            }
            var errorText = error.GetAwaiter().GetResult();
            if (process.ExitCode == 1 && allowMissing && string.IsNullOrWhiteSpace(errorText)) return null;
            if (process.ExitCode != 0) throw new IOException("Coffre Linux indisponible. Installez libsecret-tools et déverrouillez le trousseau pour mémoriser ou effacer une connexion.");
            return output.GetAwaiter().GetResult();
        }
        catch (Win32Exception e)
        {
            if (allowMissing && e.NativeErrorCode == 2) return null;
            throw new IOException("Installez libsecret-tools pour mémoriser la connexion dans le coffre Linux.", e);
        }
    }

    private sealed record Stored(string Server, string Account, string AccessToken, DateTimeOffset ExpiresAt);
}
