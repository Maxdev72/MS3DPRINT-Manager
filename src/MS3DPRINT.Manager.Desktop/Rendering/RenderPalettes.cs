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
    /// The turn that sets the part on its widest footprint: for each candidate direction (the six axes and the largest families
    /// of flat faces), the points touching the floor are gathered and the area of their outline measured — four feet span their
    /// whole circle, a flat base its face, a curved side only a strip. Near-equal footprints keep the one already down, then the
    /// one down for a file drawn Y up. Identity when the part already rests on its best footprint.
    /// </summary>
    public static Quaternion AutoStraighten(MeshModel mesh)
    {
        var families = new Dictionary<(int, int, int), (Vector3 Normal, double Area)>();
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            var normal = mesh.Normals[t];
            if (normal == Vector3.Zero) continue;
            var a = mesh.Positions[mesh.Indices[t * 3]];
            var area = Vector3.Cross(mesh.Positions[mesh.Indices[t * 3 + 1]] - a, mesh.Positions[mesh.Indices[t * 3 + 2]] - a).Length() / 2;
            var key = ((int)MathF.Round(normal.X * 50), (int)MathF.Round(normal.Y * 50), (int)MathF.Round(normal.Z * 50));
            families[key] = families.TryGetValue(key, out var known) ? (known.Normal, known.Area + area) : (normal, area);
        }
        Vector3[] axes = [-Vector3.UnitZ, Vector3.UnitZ, -Vector3.UnitY, Vector3.UnitY, -Vector3.UnitX, Vector3.UnitX];
        var candidates = axes.Concat(families.Values.OrderByDescending(f => f.Area).Take(12).Select(f => Vector3.Normalize(f.Normal)))
            .Select(down => (Down: down, Area: Footprint(mesh, down)))
            .ToList();
        var best = candidates.Max(c => c.Area);
        if (best <= 0) return Quaternion.Identity;
        var chosen = candidates.Where(c => c.Area >= best * 0.9)
            .OrderByDescending(c => Vector3.Dot(c.Down, -Vector3.UnitZ) > 0.999f)
            .ThenByDescending(c => Vector3.Dot(c.Down, -Vector3.UnitY))
            .First();
        return Vector3.Dot(chosen.Down, -Vector3.UnitZ) > 0.999f ? Quaternion.Identity : OrientationMath.Between(chosen.Down, -Vector3.UnitZ);
    }

    /// <summary>Area of the outline of the points within 0.5 % of the part's size of the floor when <paramref name="down"/> points down.</summary>
    public static double Footprint(MeshModel mesh, Vector3 down)
    {
        var reach = mesh.Positions.Max(p => Vector3.Dot(p, down));
        var tolerance = mesh.Size.Length() * 0.005f;
        var u = Vector3.Normalize(Vector3.Cross(down, MathF.Abs(down.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        var v = Vector3.Cross(down, u);
        var points = mesh.Positions.Where(p => Vector3.Dot(p, down) >= reach - tolerance)
            .Select(p => (X: (double)Vector3.Dot(p, u), Y: (double)Vector3.Dot(p, v))).Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (points.Count < 3) return 0;
        // Monotone chain convex hull, then the shoelace area.
        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new List<(double X, double Y)>();
        foreach (var pass in new[] { points, Enumerable.Reverse(points).ToList() })
        {
            var start = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        double area = 0;
        for (var i = 0; i < hull.Count; i++) { var (a, b) = (hull[i], hull[(i + 1) % hull.Count]); area += a.X * b.Y - b.X * a.Y; }
        return Math.Abs(area) / 2;
    }
}
