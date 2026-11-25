using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MS3DPRINT.Manager.Core.Printers;

/// <summary>One AMS slot: material type and colour as RRGGBB.</summary>
public sealed record BambuTray(string Type, string ColorHex);

/// <summary>
/// One HMS alert as the printer reports it. Its code reads like Bambu's "HMS_0500_0100_0003_0004"; the third group is
/// the severity (1 fatal, 2 serious, 3 common, 4 information).
/// </summary>
public sealed record BambuAlert(long Attr, long Code, DateTime? Since = null)
{
    /// <summary>Key of Bambu's HMS catalogue: attribute then code, 8 hexadecimal digits each.</summary>
    public string Ecode => $"{Attr:X8}{Code:X8}";
    public string DisplayCode => "HMS_" + string.Join('_', Enumerable.Range(0, 4).Select(i => Ecode.Substring(i * 4, 4)));
    public string Severity => (Code >> 16) switch { 1 => "Fatale", 2 => "Grave", 3 => "Courante", 4 => "Information", _ => "Alerte" };
}

public sealed record BambuStatus(string State, int? Percent, int? RemainingMinutes, int? Layer, int? TotalLayers, string? JobName,
    double? Nozzle, double? NozzleTarget, double? Bed, double? BedTarget, double? Chamber, IReadOnlyList<BambuAlert> Alerts, IReadOnlyList<BambuTray> Trays)
{
    public int ErrorCount => Alerts.Count;

    public bool IsPrinting => State is "RUNNING" or "PREPARE" or "SLICING" or "PAUSE";

    public string StateLabel => State switch
    {
        "RUNNING" => "En impression", "PAUSE" => "En pause", "FINISH" => "Terminée", "FAILED" => "Échec",
        "PREPARE" or "SLICING" => "Préparation", "IDLE" => "En attente", "" => "État inconnu", _ => State
    };
}

/// <summary>
/// Reads the "device/{serial}/report" messages of a Bambu Lab printer. The printer sends its full state once (pushall),
/// then only the fields that changed, so every report is merged into one document before reading it.
/// </summary>
public sealed class BambuReport
{
    private readonly JsonObject _print = new();

    /// <summary>The merged status, or null when the message carries no print state.</summary>
    public BambuStatus? Apply(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return null; }
        if (root?["print"] is not JsonObject print) return null;
        Merge(_print, print);
        return Read(_print);
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source.ToArray())
        {
            if (value is JsonObject child && target[key] is JsonObject existing) Merge(existing, child);
            else target[key] = value?.DeepClone();
        }
    }

    private static BambuStatus Read(JsonObject p)
    {
        var trays = new List<BambuTray>();
        if (p["ams"]?["ams"] is JsonArray units)
            foreach (var unit in units)
                if (unit?["tray"] is JsonArray slots)
                    foreach (var slot in slots)
                        if (Text(slot?["tray_type"]) is { Length: > 0 } type)
                            trays.Add(new BambuTray(type, (Text(slot?["tray_color"]) ?? "").PadRight(6, '0')[..6]));
        return new BambuStatus(Text(p["gcode_state"]) ?? "", Int(p["mc_percent"]), Int(p["mc_remaining_time"]), Int(p["layer_num"]), Int(p["total_layer_num"]),
            Text(p["subtask_name"]) is { Length: > 0 } job ? job : null,
            Number(p["nozzle_temper"]), Number(p["nozzle_target_temper"]), Number(p["bed_temper"]), Number(p["bed_target_temper"]), Number(p["chamber_temper"]),
            Alerts(p["hms"]), trays);
    }

    private static IReadOnlyList<BambuAlert> Alerts(JsonNode? hms)
    {
        if (hms is not JsonArray items) return [];
        var alerts = new List<BambuAlert>();
        foreach (var item in items)
        {
            if (Number(item?["attr"]) is not { } attr || Number(item?["code"]) is not { } code) continue;
            // "ts_unix" is a local timestamp written as yyyyMMddHHmmss despite its name.
            var since = DateTime.TryParseExact(Text(item?["ts_unix"]), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var when) ? when : (DateTime?)null;
            alerts.Add(new BambuAlert((long)attr, (long)code, since));
        }
        return alerts;
    }

    private static string? Text(JsonNode? node) => node is JsonValue value ? value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString() : null;

    private static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.GetValueKind() == JsonValueKind.Number) return value.GetValue<double>();
        return double.TryParse(Text(node), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static int? Int(JsonNode? node) => Number(node) is { } number ? (int)Math.Round(number) : null;
}

/// <summary>
/// Bambu's public HMS catalogue (https://e.bambulab.com/query.php?lang=fr): the French text of each alert code.
/// </summary>
public static class BambuHmsCatalog
{
    /// <summary>Alert code (16 hex digits) to text; codes without a text are left out.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string json)
    {
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (JsonNode.Parse(json)?["data"]?["device_hms"] is not JsonObject hms) return texts;
            foreach (var (_, list) in hms)
                if (list is JsonArray entries)
                    foreach (var entry in entries)
                        if (entry?["ecode"]?.GetValue<string>() is { Length: 16 } code && entry["intro"]?.GetValue<string>() is { Length: > 0 } intro)
                            texts[code] = intro.Trim();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) { }
        return texts;
    }

    public static string Describe(BambuAlert alert, IReadOnlyDictionary<string, string> texts) =>
        texts.TryGetValue(alert.Ecode, out var text) ? text : "Alerte sans description publiée par Bambu Lab (" + alert.DisplayCode + ").";
}
