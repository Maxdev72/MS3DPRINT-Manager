using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Naming;

namespace MS3DPRINT.Manager.Core.Storage;

public sealed class ClientCodeRegistry
{
    private readonly string _dataDirectory;
    private readonly string _activePath;
    private readonly string _previousPath;
    private readonly string _mutexName;

    public ClientCodeRegistry(string? dataDirectory = null)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "data"));
        _activePath = Path.Combine(_dataDirectory, "client-codes.json");
        _previousPath = Path.Combine(_dataDirectory, "client-codes.previous.json");
        var pathHash = SHA256.HashData(Encoding.UTF8.GetBytes(_activePath.ToUpperInvariant()));
        _mutexName = "MS3DPRINT-ClientCodes-" + Convert.ToHexString(pathHash);
    }

    public string? GetCode(string clientName)
    {
        var key = NormalizeRequired(clientName, nameof(clientName));
        var codes = ReadCodes();
        return codes.GetValueOrDefault(key);
    }

    public void Add(string clientName, string code) => Add(clientName, code, () => { });

    /// <summary>Checks the reserved name under the registry lock before creating its client.</summary>
    public void Add(string clientName, string code, Action createClient)
    {
        ArgumentNullException.ThrowIfNull(createClient);
        var key = NormalizeRequired(clientName, nameof(clientName));
        var normalizedCode = NormalizeRequired(code, nameof(code));
        Directory.CreateDirectory(_dataDirectory);

        using var mutex = new Mutex(false, _mutexName);
        try
        {
            mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // The previous writer exited; reread the active JSON before updating it.
        }

        try
        {
            var codes = ReadCodes();
            if (codes.ContainsKey(key))
            {
                throw new FolderConflictException($"Un code est déjà enregistré pour le client {key}.");
            }

            createClient();
            codes.Add(key, normalizedCode);
            var temporaryPath = Path.Combine(_dataDirectory, "client-codes." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, codes, new JsonSerializerOptions { WriteIndented = true });
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(_activePath))
                {
                    PreserveExistingBackup();
                    File.Replace(temporaryPath, _activePath, _previousPath);
                }
                else
                {
                    File.Move(temporaryPath, _activePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private Dictionary<string, string> ReadCodes()
    {
        if (!File.Exists(_activePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var stream = new FileStream(_activePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new JsonException("Le registre des codes clients est vide ou invalide.");
    }

    private void PreserveExistingBackup()
    {
        if (!File.Exists(_previousPath))
        {
            return;
        }

        var archivePath = Path.Combine(_dataDirectory, "client-codes.previous." + Guid.NewGuid().ToString("N") + ".json");
        File.Move(_previousPath, archivePath);
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = NameNormalizer.Normalize(value);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("La valeur doit contenir au moins une lettre ou un chiffre.", parameterName);
        }

        return normalized;
    }
}
