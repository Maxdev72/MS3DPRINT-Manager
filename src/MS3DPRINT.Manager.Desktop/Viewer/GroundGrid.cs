using System.Numerics;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>
/// Reference grid lying on the base of the model (its lowest Z). Lines fade into the background colour with distance, so the floor
/// seems to go on for ever without needing blending; every fifth line is brighter.
/// </summary>
public static class GroundGrid
{
    private const int MaxCellsPerSide = 120;
    private const int SegmentsPerLine = 40;
    private static readonly Vector3 Minor = new(0.20f, 0.27f, 0.35f);
    private static readonly Vector3 Major = new(0.31f, 0.41f, 0.53f);

    /// <summary>A round cell size (1, 2, 5 × 10ⁿ millimetres) giving about ten cells across the model.</summary>
    public static double NiceSpacingMillimetres(double largestExtentMillimetres)
    {
        if (!double.IsFinite(largestExtentMillimetres) || largestExtentMillimetres <= 0) return 10;
        var raw = largestExtentMillimetres / 10;
        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var mantissa = raw / power;
        var nice = mantissa <= 1 ? 1 : mantissa <= 2 ? 2 : mantissa <= 5 ? 5 : 10;
        return nice * power;
    }

    /// <summary>Line end points (pairs of vertices, for GL_LINES) and one RGB colour per vertex, in model units.</summary>
    public static (float[] Positions, float[] Colors) Build(MeshModel mesh, float spacing, Vector3 background)
    {
        var radius = mesh.Radius;
        if (!(spacing > 0) || !float.IsFinite(spacing)) spacing = radius / 5;
        var cells = (int)Math.Clamp(Math.Ceiling(Math.Max(6 * radius, 10 * spacing) / spacing), 10, MaxCellsPerSide);
        var half = cells * spacing;
        var centreX = MathF.Round(mesh.Center.X / spacing) * spacing;
        var centreY = MathF.Round(mesh.Center.Y / spacing) * spacing;
        // a hair under the base so that a flat bottom does not flicker against the grid
        var z = mesh.Min.Z - radius * 0.0005f;

        var lines = 2 * (2 * cells + 1);
        var positions = new float[lines * SegmentsPerLine * 2 * 3];
        var colors = new float[positions.Length];
        var at = 0;

        void Add(float x, float y, float fade, Vector3 baseColor)
        {
            var color = Vector3.Lerp(background, baseColor, fade);
            positions[at] = x; positions[at + 1] = y; positions[at + 2] = z;
            colors[at] = color.X; colors[at + 1] = color.Y; colors[at + 2] = color.Z;
            at += 3;
        }

        float Fade(float x, float y)
        {
            var t = Math.Clamp(MathF.Sqrt((x - centreX) * (x - centreX) + (y - centreY) * (y - centreY)) / half, 0f, 1f);
            var s = Math.Clamp((t - 0.25f) / 0.75f, 0f, 1f);
            return 1 - s * s * (3 - 2 * s);
        }

        for (var axis = 0; axis < 2; axis++)
            for (var i = -cells; i <= cells; i++)
            {
                var along = (axis == 0 ? centreX : centreY) + i * spacing;
                var index = (long)MathF.Round(along / spacing);
                var color = index % 5 == 0 ? Major : Minor;
                for (var segment = 0; segment < SegmentsPerLine; segment++)
                    for (var end = 0; end < 2; end++)
                    {
                        var other = (axis == 0 ? centreY : centreX) - half + 2 * half * (segment + end) / SegmentsPerLine;
                        var (x, y) = axis == 0 ? (along, other) : (other, along);
                        Add(x, y, Fade(x, y), color);
                    }
            }
        return (positions, colors);
    }
}
