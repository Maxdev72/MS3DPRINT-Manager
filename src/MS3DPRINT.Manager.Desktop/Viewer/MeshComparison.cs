using System.Globalization;
using System.Numerics;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>One line of the comparison table: a quantity for both versions and how the second differs from the first.</summary>
public sealed record ComparisonRow(string Label, string First, string Second, string Difference);

/// <summary>Compares two versions of a model: size, volume, surface and triangle count.</summary>
public static class MeshComparison
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static IReadOnlyList<ComparisonRow> Compare(MeshModel first, MeshModel second, MeshMeasures firstMeasures, MeshMeasures secondMeasures, double mmPerUnit)
    {
        var mm3 = mmPerUnit * mmPerUnit * mmPerUnit / 1000.0; // model unit³ → cm³
        var mm2 = mmPerUnit * mmPerUnit / 100.0;              // model unit² → cm²
        return
        [
            Length("Largeur X", first.Size.X * mmPerUnit, second.Size.X * mmPerUnit),
            Length("Profondeur Y", first.Size.Y * mmPerUnit, second.Size.Y * mmPerUnit),
            Length("Hauteur Z", first.Size.Z * mmPerUnit, second.Size.Z * mmPerUnit),
            Quantity("Volume", firstMeasures.Volume * mm3, secondMeasures.Volume * mm3, "cm³"),
            Quantity("Surface", firstMeasures.SurfaceArea * mm2, secondMeasures.SurfaceArea * mm2, "cm²"),
            new("Triangles", first.TriangleCount.ToString("N0", French), second.TriangleCount.ToString("N0", French), Signed(second.TriangleCount - first.TriangleCount, 0, "")),
        ];
    }

    /// <summary>Translation that puts the second model's bounding-box centre on the first one's.</summary>
    public static Vector3 CentreOffset(MeshModel first, MeshModel second) => first.Center - second.Center;

    private static ComparisonRow Length(string label, double first, double second)
        => new(label, Format(first) + " mm", Format(second) + " mm", Signed(second - first, 2, " mm"));

    private static ComparisonRow Quantity(string label, double first, double second, string unit)
    {
        var percent = Math.Abs(first) < 1e-12 ? string.Empty : $" ({Signed((second - first) / first * 100, 1, " %")})";
        return new(label, Format(first) + " " + unit, Format(second) + " " + unit, Signed(second - first, 2, " " + unit) + percent);
    }

    private static string Format(double value) => value.ToString("0.##", French);

    /// <summary>Signed difference, with a true minus sign; "identique" when it rounds to zero.</summary>
    public static string Signed(double value, int decimals, string unit)
    {
        var rounded = Math.Round(value, decimals);
        if (rounded == 0) return "identique";
        var text = Math.Abs(rounded).ToString(decimals == 0 ? "N0" : "0." + new string('#', decimals), French);
        return (rounded > 0 ? "+" : "−") + text + unit;
    }
}
