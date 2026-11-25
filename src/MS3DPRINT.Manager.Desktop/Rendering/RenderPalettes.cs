using System.Numerics;
using System.Text.Json;
using MS3DPRINT.Estimator;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>A group of filament colours sharing one finish (Mat, Brillant, Soie, Phosphorescent).</summary>
public sealed record RenderPaletteGroup(string Name, string Finish, IReadOnlyList<RenderColor> Colors);

/// <summary>The filament palettes bundled with the application (filament-palettes.json).</summary>
public static class RenderPalettes
{
    public static IReadOnlyList<RenderPaletteGroup> All { get; } = Load();

    private static IReadOnlyList<RenderPaletteGroup> Load()
    {
        using var stream = typeof(RenderPalettes).Assembly.GetManifestResourceStream("MS3DPRINT.Manager.Desktop.Rendering.filament-palettes.json")
            ?? throw new InvalidOperationException("Palettes de filament absentes de l’application.");
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.GetProperty("groupes").EnumerateArray().Select(group =>
        {
            var finish = group.GetProperty("finition").GetString()!;
            return new RenderPaletteGroup(group.GetProperty("nom").GetString()!, finish, group.GetProperty("couleurs").EnumerateArray()
                .Select(color => new RenderColor(color.GetProperty("nom").GetString()!, color.GetProperty("hex").GetString()!.ToUpperInvariant(), finish)).ToList());
        }).ToList();
    }
}

/// <summary>Standing a part the way it would sit on a table.</summary>
public static class RenderOrientation
{
    /// <summary>
    /// The turn that lays the part on its largest flat base: among the flat faces that touch the part's outline on their side
    /// (a real base, not an inner shelf), the direction with the most area goes down. Near-equal bases keep the one already
    /// down, then the one down for a file drawn Y up. Identity when the part already rests on its best base.
    /// </summary>
    public static Quaternion AutoStraighten(MeshModel mesh)
    {
        var areas = new Dictionary<(int, int, int), (Vector3 Normal, double Area)>();
        var count = mesh.TriangleCount;
        for (var t = 0; t < count; t++)
        {
            var normal = mesh.Normals[t];
            if (normal == Vector3.Zero) continue;
            var a = mesh.Positions[mesh.Indices[t * 3]];
            var area = Vector3.Cross(mesh.Positions[mesh.Indices[t * 3 + 1]] - a, mesh.Positions[mesh.Indices[t * 3 + 2]] - a).Length() / 2;
            var key = ((int)MathF.Round(normal.X * 50), (int)MathF.Round(normal.Y * 50), (int)MathF.Round(normal.Z * 50));
            areas[key] = areas.TryGetValue(key, out var known) ? (known.Normal, known.Area + area) : (normal, area);
        }
        var tolerance = mesh.Radius * 0.01f;
        var bases = areas.Values
            .Where(family => family.Area > 0)
            .OrderByDescending(family => family.Area)
            .Take(12)
            .Select(family => (family.Normal, Area: BaseArea(mesh, family.Normal, tolerance)))
            .Where(candidate => candidate.Area > 0)
            .ToList();
        if (bases.Count == 0) return Quaternion.Identity;
        var best = bases.Max(candidate => candidate.Area);
        var chosen = bases.Where(candidate => candidate.Area >= best * 0.9)
            .OrderByDescending(candidate => Vector3.Dot(candidate.Normal, -Vector3.UnitZ) > 0.999f)
            .ThenByDescending(candidate => Vector3.Dot(candidate.Normal, -Vector3.UnitY))
            .First();
        return Vector3.Dot(chosen.Normal, -Vector3.UnitZ) > 0.999f ? Quaternion.Identity : OrientationMath.Between(chosen.Normal, -Vector3.UnitZ);
    }

    /// <summary>Area of the faces facing <paramref name="normal"/> that lie on the part's outline in that direction.</summary>
    private static double BaseArea(MeshModel mesh, Vector3 normal, float tolerance)
    {
        var reach = mesh.Positions.Max(p => Vector3.Dot(p, normal));
        double area = 0;
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            if (Vector3.Dot(mesh.Normals[t], normal) < 0.999f) continue;
            var a = mesh.Positions[mesh.Indices[t * 3]];
            var b = mesh.Positions[mesh.Indices[t * 3 + 1]];
            var c = mesh.Positions[mesh.Indices[t * 3 + 2]];
            if (Vector3.Dot((a + b + c) / 3, normal) < reach - tolerance) continue;
            area += Vector3.Cross(b - a, c - a).Length() / 2;
        }
        return area;
    }
}
