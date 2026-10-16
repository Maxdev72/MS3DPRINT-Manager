using System.Numerics;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>
/// Reference grid lying on the base of the model (its lowest Z). It is drawn by the graphics card for every pixel (see
/// <c>GpuMeshViewport</c>): the floor really goes on to the horizon at any zoom, with coarser lines taking over as the view backs away.
/// </summary>
public static class GroundGrid
{
    /// <summary>Colour of the lines of one cell and of the coarser lines, as seen on the dark background.</summary>
    public static readonly Vector3 Minor = new(0.20f, 0.27f, 0.35f);
    public static readonly Vector3 Major = new(0.31f, 0.41f, 0.53f);

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

    /// <summary>Cell size to draw: the one asked for, else a fifth of the model radius.</summary>
    public static float Spacing(float requested, float modelRadius) =>
        requested > 0 && float.IsFinite(requested) ? requested : Math.Max(modelRadius / 5, 1e-6f);
}
