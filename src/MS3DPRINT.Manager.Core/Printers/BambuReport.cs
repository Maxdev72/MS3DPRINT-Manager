using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MS3DPRINT.Manager.Core.Printers;

/// <summary>One AMS slot: material type and colour as RRGGBB.</summary>
public sealed record BambuTray(string Type, string ColorHex);

public sealed record BambuStatus(string State, int? Percent, int? RemainingMinutes, int? Layer, int? TotalLayers, string? JobName,
    double? Nozzle, double? NozzleTarget, double? Bed, double? BedTarget, double? Chamber, int ErrorCount, IReadOnlyList<BambuTray> Trays)
{
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
            p["hms"] is JsonArray hms ? hms.Count : 0, trays);
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
