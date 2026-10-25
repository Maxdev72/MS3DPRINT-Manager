using System.Text.Json;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Printers;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

public sealed record DraftMachine(string? Name, string? Hours, string? Rate, string? Maintenance, string? Power, Guid? PrinterId);
public sealed record DraftCost(string? Name, string? Amount, bool Repeated, bool Enabled);
public sealed record EstimateDraft(Guid Id, DateTimeOffset UpdatedAt, Dictionary<string, string?> Fields,
    int EngineVersion, int Scope, int DataMode, FilamentProfile? Filament, PrinterProfile? Printer,
    string? ProjectReference, string? ClientName, EstimateV2Options Options, bool IncludesSupports, bool Outsourced,
    DraftMachine[] Machines, DraftCost[] Costs, EstimateProfile? Editing, EstimateInputs? Inputs, EstimateResult? Result, int ChargeCategory = 0, bool IsDuplicate = false);

/// <summary>Unvalidated form text stays separate from committed estimates. Writes replace a complete file atomically.</summary>
public sealed class EstimateDraftStore(string workspaceRoot)
{
    private readonly string _directory = Path.Combine(Path.GetFullPath(workspaceRoot), ".ms3dprint-manager", "estimate-drafts");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private string PathFor(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Le brouillon doit avoir un identifiant.");
        return Path.Combine(_directory, id + ".json");
    }

    public void Save(EstimateDraft draft)
    {
        var path = PathFor(draft.Id);
        WorkspacePathSafety.EnsureNoLinks(path);
        Directory.CreateDirectory(_directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, draft, Options);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public IReadOnlyList<EstimateDraft> LoadReadable()
    {
        WorkspacePathSafety.EnsureNoLinks(_directory);
        if (!Directory.Exists(_directory)) return [];
        var drafts = new List<EstimateDraft>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                WorkspacePathSafety.EnsureNoLinks(path);
                using var stream = File.OpenRead(path);
                var draft = JsonSerializer.Deserialize<EstimateDraft>(stream, Options);
                if (draft is not null && draft.Id != Guid.Empty && draft.Fields is not null && draft.Machines is not null && draft.Costs is not null
                    && draft.Options is not null && draft.EngineVersion is 1 or 2 && draft.Scope is >= 0 and <= 2 && draft.DataMode is 0 or 1)
                    if (Path.GetFileNameWithoutExtension(path).Equals(draft.Id.ToString(), StringComparison.OrdinalIgnoreCase)
                        && draft.Machines.All(m => m is not null) && draft.Costs.All(c => c is not null)) drafts.Add(draft);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        }
        return drafts.OrderByDescending(draft => draft.UpdatedAt).ToArray();
    }

    // Promotion to a saved quote consumes its working draft; explicit discard is confirmed by the UI.
    public void Remove(Guid id)
    {
        var path = PathFor(id);
        WorkspacePathSafety.EnsureNoLinks(path);
        File.Delete(path);
    }
}
