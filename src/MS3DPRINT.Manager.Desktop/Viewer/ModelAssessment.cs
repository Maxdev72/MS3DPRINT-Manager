using System.Globalization;
using System.Text.RegularExpressions;
using MS3DPRINT.Estimator;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Glue between what the viewer holds (a mesh in model units, text fields) and the estimator and feasibility engine.</summary>
public static partial class ModelAssessment
{
    /// <summary>Nozzle the wall thickness field is divided by to get a number of loops, as when the slicer is run.</summary>
    public const double ReferenceNozzleMm = 0.4;
    public const double DefaultLayerHeightMm = 0.2;

    /// <summary>
    /// How fast the head of the printer really moves compared with the average of the five printers the engine was fitted on.
    /// Measured on Bambu Studio: P1S, X1 Carbon and H2S come out about 1.35 times the average, A1 and A1 mini about 0.6.
    /// </summary>
    public static readonly IReadOnlyList<(string Label, double Factor)> SpeedClasses =
    [
        ("Moyenne (par défaut)", 1.0),
        ("Rapide : CoreXY à fortes accélérations (P1S, X1C, H2S…)", 1.35),
        ("Lente : imprimante bridée (A1…)", 0.6)
    ];

    /// <summary>The viewer's mesh in millimetres, ready for the engine.</summary>
    public static PrintMesh ToPrintMesh(MeshModel mesh, double millimetresPerUnit)
    {
        var print = new PrintMesh(mesh.Positions, mesh.Indices);
        return Math.Abs(millimetresPerUnit - 1) < 1e-9 ? print : print.Scaled((float)millimetresPerUnit);
    }

    /// <summary>Layer height written at the start of a slicer quality name such as "0.20mm Standard @BBL X1C"; 0.2 mm when there is none.</summary>
    public static double LayerHeightFrom(string? quality)
    {
        var match = LayerHeightName().Match(quality ?? string.Empty);
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var height) && height is >= 0.04 and <= 1.0
            ? height : DefaultLayerHeightMm;
    }

    /// <summary>Number of wall loops for a wall thickness in millimetres, at least one.</summary>
    public static int WallLoopsFrom(double wallMm) => Math.Max(1, (int)Math.Round(wallMm / ReferenceNozzleMm));

    public static string Describe(FeasibilityVerdict verdict) => verdict switch
    {
        FeasibilityVerdict.Printable => "Imprimable",
        FeasibilityVerdict.PrintableWithWarnings => "Imprimable avec réserves",
        _ => "À corriger avant impression"
    };

    /// <summary>A few words for the Infos tab: the verdict and how many findings stand behind it.</summary>
    public static string Summarize(FeasibilityReport report)
    {
        var findings = report.Issues.Count(issue => issue.Severity != FeasibilitySeverity.Info);
        return findings == 0 ? Describe(report.Verdict) : $"{Describe(report.Verdict)} ({findings} point{(findings > 1 ? "s" : string.Empty)} à voir)";
    }

    [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?)\s*mm", RegexOptions.CultureInvariant)]
    private static partial Regex LayerHeightName();
}
