using System.Text.Json;

namespace MS3DPRINT.Manager.Desktop.Printers;

/// <summary>Local network access to a Bambu Lab printer (LAN address, serial number, access code shown on its screen).</summary>
public sealed record PrinterConnection(string Host, string Serial, string AccessCode);

/// <summary>
/// Kept on this PC only, not in the workspace: the address is only valid on this network and the access code must not
/// be synced to the server or other devices.
/// shortcut: the access code is stored in clear in the user's profile, encrypt it (DPAPI / libsecret) if that becomes a concern.
/// </summary>
public sealed class PrinterConnectionStore(string path)
{
    public static PrinterConnectionStore Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "MS3DPRINT Manager", "printer-connections.json"));

    public PrinterConnection? Get(Guid printerId) => Load().TryGetValue(printerId, out var value) ? value : null;

    public void Set(Guid printerId, PrinterConnection? connection)
    {
        var all = Load();
        if (connection is null) all.Remove(printerId); else all[printerId] = connection;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(all));
    }

    private Dictionary<Guid, PrinterConnection> Load()
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<Guid, PrinterConnection>>(File.ReadAllText(path)) ?? [] : []; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
}
