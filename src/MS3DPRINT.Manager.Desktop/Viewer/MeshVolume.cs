using System.Numerics;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Volume and surface of a mesh, in model units (cubed and squared); <see cref="IsClosed"/> is null when the check was skipped because the mesh is very large.</summary>
public readonly record struct MeshMeasures(double Volume, double SurfaceArea, bool? IsClosed, bool Inverted = false);

/// <summary>A material the weight can be estimated for, optionally tied to a catalogue filament and its price.</summary>
public sealed record MaterialChoice(string Label, double Density, decimal? PricePerKg = null, string? FilamentKey = null, string? MaterialName = null, double? MaxFlowMm3S = null);

/// <summary>What the viewer hands to the estimate wizard: the model name, its estimated weight and the chosen filament.</summary>
public sealed record ModelEstimateSeed(string Name, double? WeightGrams, string? FilamentKey, string ModelPath, double? DurationHours = null, string? PrinterModel = null);

public static class MeshVolume
{
    private const int MaxClosureCheckTriangles = 1_000_000;

    /// <summary>Densities in g/cm³ for the usual filaments (typical datasheet values).</summary>
    public static readonly IReadOnlyList<MaterialChoice> Presets =
    [
        new("PLA (1,24 g/cm³)", 1.24, MaterialName: "PLA", MaxFlowMm3S: MaxFlowFor("PLA")), new("PETG (1,27 g/cm³)", 1.27, MaterialName: "PETG", MaxFlowMm3S: MaxFlowFor("PETG")),
        new("ABS (1,04 g/cm³)", 1.04, MaterialName: "ABS", MaxFlowMm3S: MaxFlowFor("ABS")), new("ASA (1,07 g/cm³)", 1.07, MaterialName: "ASA", MaxFlowMm3S: MaxFlowFor("ASA")),
        new("TPU (1,21 g/cm³)", 1.21, MaterialName: "TPU", MaxFlowMm3S: MaxFlowFor("TPU")), new("Nylon PA (1,14 g/cm³)", 1.14, MaterialName: "PA", MaxFlowMm3S: MaxFlowFor("PA")),
        new("PC (1,20 g/cm³)", 1.20, MaterialName: "PC", MaxFlowMm3S: MaxFlowFor("PC")), new("PVA (1,23 g/cm³)", 1.23, MaterialName: "PVA", MaxFlowMm3S: MaxFlowFor("PVA"))
    ];

    /// <summary>
    /// What the hotend can melt per second for a material family (mm³/s), typical datasheet values: PLA and PETG match the
    /// Bambu Studio profiles measured on five printers (PLA 21 to 25, PETG 13 to 18); PLA 20 and PETG 14 are on the cautious side.
    /// </summary>
    public static double MaxFlowFor(string? material)
    {
        var name = (material ?? string.Empty).ToUpperInvariant();
        foreach (var (key, flow) in new (string, double)[] { ("TPU", 3.6), ("PETG", 14), ("ABS", 16), ("ASA", 16), ("NYLON", 10), ("PA", 10), ("PC", 11), ("PVA", 6), ("PLA", 20) })
            if (name.Contains(key, StringComparison.Ordinal)) return flow;
        return 20;
    }

    /// <summary>Density for a catalogue material name such as "PLA+" or "PETG-CF"; PLA when unknown.</summary>
    public static double DensityFor(string? material)
    {
        var name = (material ?? string.Empty).ToUpperInvariant();
        foreach (var (key, density) in new (string, double)[] { ("PETG", 1.27), ("ABS", 1.04), ("ASA", 1.07), ("TPU", 1.21), ("NYLON", 1.14), ("PA", 1.14), ("PC", 1.20), ("PVA", 1.23), ("PLA", 1.24) })
            if (name.Contains(key, StringComparison.Ordinal)) return density;
        return 1.24;
    }

    /// <summary>
    /// Signed tetrahedra against the centre give the enclosed volume of a closed mesh; the surface is the sum of the
    /// triangle areas. The mesh is closed when every edge is shared by exactly two triangles.
    /// </summary>
    public static MeshMeasures Compute(MeshModel mesh, CancellationToken cancellationToken = default)
    {
        var positions = mesh.Positions;
        var indices = mesh.Indices;
        var origin = mesh.Center;
        double volume = 0, area = 0;
        for (var t = 0; t + 2 < indices.Length; t += 3)
        {
            if ((t & 0xFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = positions[indices[t]] - origin;
            var b = positions[indices[t + 1]] - origin;
            var c = positions[indices[t + 2]] - origin;
            volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            area += Vector3.Cross(b - a, c - a).Length() / 2.0;
        }

        if (indices.Length / 3 > MaxClosureCheckTriangles) return new MeshMeasures(Math.Abs(volume), area, null, volume < 0);

        // STL stores every triangle with its own corners: weld identical positions before looking at shared edges.
        var ids = new Dictionary<Vector3, int>(positions.Length / 2);
        var welded = new int[indices.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            if ((i & 0xFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var corner = positions[indices[i]];
            if (!ids.TryGetValue(corner, out var id)) { id = ids.Count; ids[corner] = id; }
            welded[i] = id;
        }

        var edges = new ulong[welded.Length];
        for (var t = 0; t + 2 < welded.Length; t += 3)
        {
            edges[t] = Key(welded[t], welded[t + 1]);
            edges[t + 1] = Key(welded[t + 1], welded[t + 2]);
            edges[t + 2] = Key(welded[t + 2], welded[t]);
        }
        cancellationToken.ThrowIfCancellationRequested();
        Array.Sort(edges);
        var closed = edges.Length > 0;
        for (var i = 0; i < edges.Length && closed;)
        {
            var next = i;
            while (next < edges.Length && edges[next] == edges[i]) next++;
            if (next - i != 2) closed = false;
            i = next;
        }
        return new MeshMeasures(Math.Abs(volume), area, closed, volume < 0);
    }

    private static ulong Key(int a, int b) => a < b ? ((ulong)(uint)a << 32) | (uint)b : ((ulong)(uint)b << 32) | (uint)a;

    /// <summary>
    /// Printed mass in grams: an outer shell of <paramref name="wallMm"/> printed solid, the rest filled at
    /// <paramref name="infillPercent"/>. Volume in cm³ and area in cm²; indicative, a slicer is more exact.
    /// </summary>
    public static double EstimateMass(double volumeCm3, double areaCm2, double density, double infillPercent, double wallMm)
    {
        var shell = Math.Min(volumeCm3, areaCm2 * wallMm / 10.0);
        var inner = volumeCm3 - shell;
        return density * (shell + inner * Math.Clamp(infillPercent, 0, 100) / 100.0);
    }
}
