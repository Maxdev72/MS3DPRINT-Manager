using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Build volume of a printer: width (X), depth (Y) and height (Z) in millimetres.</summary>
public readonly partial record struct PlateSize(double WidthMm, double DepthMm, double HeightMm)
{
    private const double Tolerance = 0.01;
    private const double MaxMillimetres = 5000;

    public static PlateSize Default => new(256, 256, 256);

    /// <summary>
    /// Reads "256 × 256 × 256 mm", "250x210x220", "25,6 x 25 x 24 cm"…: the first three numbers are width, depth and height, and a unit
    /// of centimetres multiplies them by ten. Text that is not a box (a round bed, free words) gives null.
    /// </summary>
    public static PlateSize? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Contains('Ø') || text.Contains('⌀') || text.Contains("diam", StringComparison.OrdinalIgnoreCase) || text.Contains("rond", StringComparison.OrdinalIgnoreCase)) return null;
        var numbers = Number().Matches(text);
        if (numbers.Count < 3) return null;
        var factor = Centimetres().IsMatch(text) ? 10.0 : 1.0;
        var values = new double[3];
        for (var i = 0; i < 3; i++)
        {
            if (!double.TryParse(numbers[i].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
            values[i] = value * factor;
            if (!(values[i] > 0 && values[i] <= MaxMillimetres)) return null;
        }
        return new PlateSize(values[0], values[1], values[2]);
    }

    public bool Fits(Vector3 sizeMm) => Overflow(sizeMm) == Vector3.Zero;

    /// <summary>How far a part of this size sticks out of the plate along each axis, 0 where it fits.</summary>
    public Vector3 Overflow(Vector3 sizeMm) => new(
        Excess(sizeMm.X - WidthMm),
        Excess(sizeMm.Y - DepthMm),
        Excess(sizeMm.Z - HeightMm));

    // Differences within the tolerance are rounding noise: they count as fitting, and are reported as 0.
    private static float Excess(double difference) => difference > Tolerance ? (float)difference : 0;

    public Vector3 ToVector3() => new((float)WidthMm, (float)DepthMm, (float)HeightMm);

    public string Label => string.Create(CultureInfo.InvariantCulture, $"{WidthMm:0.##} × {DepthMm:0.##} × {HeightMm:0.##} mm");

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex Number();

    [GeneratedRegex(@"\bcm\b", RegexOptions.IgnoreCase)]
    private static partial Regex Centimetres();
}
