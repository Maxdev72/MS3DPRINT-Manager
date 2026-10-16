using System.Globalization;
using System.Numerics;

namespace MS3DPRINT.Estimator;

public enum FeasibilitySeverity { Info, Warning, Error }

public enum FeasibilityVerdict { Printable, PrintableWithWarnings, NotPrintable }

public enum FeasibilityCode
{
    EmptyModel, OpenMesh, NonManifold, InconsistentWinding, InvertedMesh, DegenerateTriangles, MultipleShells,
    SuspiciousScale, TooLarge, ThinWalls, Overhangs, SmallBase, NotAnalysed
}

/// <summary>One finding. <see cref="Value"/> is the figure behind it (a thickness, an area, a count) and <see cref="Points"/> a few places on the model to highlight.</summary>
public sealed record FeasibilityIssue(FeasibilityCode Code, FeasibilitySeverity Severity, string Message, double Value = 0, IReadOnlyList<Vector3>? Points = null);

public sealed record FeasibilityReport(
    FeasibilityVerdict Verdict, IReadOnlyList<FeasibilityIssue> Issues, Vector3 SizeMm, int ShellCount,
    double? MinWallThicknessMm, double ThinAreaMm2, double OverhangAreaMm2, double SupportColumnVolumeMm3);

public sealed record FeasibilitySettings
{
    /// <summary>Size of the printer; a model larger than this on one axis cannot be printed as it lies. Not checked when null.</summary>
    public Vector3? BuildVolumeMm { get; init; }
    /// <summary>Walls thinner than this cannot be printed at all; the nozzle line width when null.</summary>
    public double? UnprintableWallMm { get; init; }
    /// <summary>Walls thinner than this are printed with a single line and stay fragile; twice the line width when null.</summary>
    public double? FragileWallMm { get; init; }
    /// <summary>Faces leaning further than this from the vertical need support.</summary>
    public double SupportAngleDegrees { get; init; } = 60;
    public double LayerHeightMm { get; init; } = 0.2;
    /// <summary>A mesh with holes is refused; when false it is only a warning, for a vendor who repairs files himself.</summary>
    public bool RequireWatertight { get; init; } = true;
    public int MaxThicknessSamples { get; init; } = 30_000;
    public int MaxHighlightPoints { get; init; } = 200;
}

/// <summary>
/// Tells whether a model can be printed, and what will go wrong if not: a mesh that is not closed, walls too thin for the nozzle,
/// overhangs that need support, a base too small to stick. Only geometry is looked at, no slicer is run.
/// </summary>
public static class FeasibilityAnalyzer
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static FeasibilityReport Analyze(PrintMesh mesh, FeasibilitySettings? settings = null, MachineProfile? machine = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        settings ??= new FeasibilitySettings();
        machine ??= new MachineProfile();
        machine.Validate();
        var issues = new List<FeasibilityIssue>();
        var size = mesh.Size;

        if (mesh.TriangleCount == 0 || !(size.X > 0) || !(size.Y > 0) || !(size.Z > 0))
        {
            issues.Add(new(FeasibilityCode.EmptyModel, FeasibilitySeverity.Error, "Le modèle n'a pas de volume : il est vide ou entièrement plat."));
            return Report(issues, size, 0, null, 0, 0, 0);
        }

        var topology = MeshTopology.Analyse(mesh, cancellationToken);
        CheckTopology(issues, topology, mesh.TriangleCount, settings);
        CheckSize(issues, size, settings);

        var largest = Math.Max(size.X, Math.Max(size.Y, size.Z));
        if (largest < 2 || largest > 1000)
            issues.Add(new(FeasibilityCode.SuspiciousScale, FeasibilitySeverity.Warning,
                $"Le modèle mesure {Mm(largest)} dans sa plus grande dimension : les unités du fichier sont peut-être incorrectes (pouces, mètres).", largest));

        double? minWall = null;
        double thinArea = 0, overhangArea = 0, supportVolume = 0;
        if (!topology.IsClean)
        {
            issues.Add(new(FeasibilityCode.NotAnalysed, FeasibilitySeverity.Info, "Les parois et les surplombs n'ont pas pu être analysés : le maillage doit d'abord être réparé."));
        }
        else
        {
            // a mesh wound inside out is turned back first, so the normals below point outward
            var oriented = topology.SignedVolume < 0 ? Flipped(mesh) : mesh;
            (minWall, thinArea) = CheckWalls(issues, oriented, topology, settings, machine, cancellationToken);
            (overhangArea, supportVolume) = CheckOverhangs(issues, oriented, topology, settings, machine, cancellationToken);
            CheckBase(issues, mesh, settings, cancellationToken);
        }
        return Report(issues, size, topology.Shells, minWall, thinArea, overhangArea, supportVolume);
    }

    private static void CheckTopology(List<FeasibilityIssue> issues, TopologyReport topology, int triangles, FeasibilitySettings settings)
    {
        var holes = settings.RequireWatertight ? FeasibilitySeverity.Error : FeasibilitySeverity.Warning;
        if (topology.BoundaryEdges > 0)
            issues.Add(new(FeasibilityCode.OpenMesh, holes,
                $"Le maillage n'est pas étanche : {topology.BoundaryEdges} arêtes ne touchent qu'une seule face (trous). La découpe donnera des résultats faux.", topology.BoundaryEdges));
        if (topology.NonManifoldEdges > 0)
            issues.Add(new(FeasibilityCode.NonManifold, holes,
                $"{topology.NonManifoldEdges} arêtes sont partagées par plus de deux faces (géométrie non valide, souvent des pièces qui se touchent).", topology.NonManifoldEdges));
        if (topology.BoundaryEdges == 0 && topology.NonManifoldEdges == 0 && topology.InconsistentEdges > 0)
            issues.Add(new(FeasibilityCode.InconsistentWinding, FeasibilitySeverity.Warning,
                $"{topology.InconsistentEdges} arêtes ont des faces orientées en sens contraire : certaines faces sont retournées.", topology.InconsistentEdges));
        if (topology.IsClean && topology.SignedVolume < 0)
            issues.Add(new(FeasibilityCode.InvertedMesh, FeasibilitySeverity.Info, "Toutes les faces regardent vers l'intérieur : l'orientation est corrigée automatiquement."));
        if (topology.DegenerateTriangles > 0)
            issues.Add(new(FeasibilityCode.DegenerateTriangles,
                topology.DegenerateTriangles > triangles / 100 ? FeasibilitySeverity.Warning : FeasibilitySeverity.Info,
                $"{topology.DegenerateTriangles} triangles sont dégénérés (sans surface) et seront ignorés.", topology.DegenerateTriangles));
        if (topology.Shells > 1)
            issues.Add(new(FeasibilityCode.MultipleShells, FeasibilitySeverity.Warning,
                $"Le fichier contient {topology.Shells} pièces séparées : vérifiez qu'elles doivent bien être imprimées ensemble.", topology.Shells));
    }

    private static void CheckSize(List<FeasibilityIssue> issues, Vector3 size, FeasibilitySettings settings)
    {
        if (settings.BuildVolumeMm is not { } volume) return;
        const float tolerance = 0.01f; // same as the plate outline: rounding noise fits
        if (size.X > volume.X + tolerance || size.Y > volume.Y + tolerance || size.Z > volume.Z + tolerance)
            issues.Add(new(FeasibilityCode.TooLarge, FeasibilitySeverity.Error,
                $"Le modèle ({Mm(size.X)} × {Mm(size.Y)} × {Mm(size.Z)}) dépasse le volume d'impression ({Mm(volume.X)} × {Mm(volume.Y)} × {Mm(volume.Z)})."));
    }

    private static (double? MinWall, double ThinArea) CheckWalls(
        List<FeasibilityIssue> issues, PrintMesh mesh, TopologyReport topology, FeasibilitySettings settings, MachineProfile machine, CancellationToken cancellationToken)
    {
        var line = machine.ExtrusionWidthMm;
        var unprintable = settings.UnprintableWallMm ?? line;
        var fragile = settings.FragileWallMm ?? 2 * line;
        var samples = WallThickness.Measure(mesh, settings.MaxThicknessSamples, cancellationToken);
        if (samples.Count == 0) return (null, 0);

        // a single sharp tip or edge is always thin: only a thickness shared by a real stretch of surface counts
        var significant = Math.Max(1.0, 0.002 * topology.SurfaceArea);
        samples.Sort((x, y) => x.ThicknessMm.CompareTo(y.ThicknessMm));
        double running = 0, minWall = samples[0].ThicknessMm;
        foreach (var sample in samples)
        {
            running += sample.AreaMm2;
            if (running >= significant) { minWall = sample.ThicknessMm; break; }
        }

        var thin = samples.Where(sample => sample.ThicknessMm < fragile).ToList();
        var thinArea = thin.Sum(sample => sample.AreaMm2);
        if (thinArea < significant) return (minWall, 0);

        // a knife edge or a pointed tip is thin over a sliver of surface and prints without it; a thin wall is thin over a large share
        var unprintableArea = samples.Where(sample => sample.ThicknessMm < unprintable).Sum(sample => sample.AreaMm2);
        var severity = unprintableArea >= Math.Max(significant, 0.03 * topology.SurfaceArea) ? FeasibilitySeverity.Error : FeasibilitySeverity.Warning;
        var message = severity == FeasibilitySeverity.Error
            ? $"Parois trop fines pour la buse : {Mm(minWall)} au plus mince (minimum {Mm(unprintable)}) sur environ {unprintableArea:0} mm². Ces zones ne s'imprimeront pas."
            : unprintableArea > 0
                ? $"Arêtes ou pointes très fines (jusqu'à {Mm(minWall)}) sur environ {unprintableArea:0} mm² : l'extrémité ne s'imprimera pas, le reste est correct."
                : $"Parois fines : {Mm(minWall)} au plus mince sur environ {thinArea:0} mm². Elles seront imprimées avec un seul trait et resteront fragiles.";
        issues.Add(new(FeasibilityCode.ThinWalls, severity, message, minWall, Pick(thin.Select(sample => sample.Position).ToList(), settings.MaxHighlightPoints)));
        return (minWall, thinArea);
    }

    private static (double OverhangArea, double ColumnVolume) CheckOverhangs(
        List<FeasibilityIssue> issues, PrintMesh mesh, TopologyReport topology, FeasibilitySettings settings, MachineProfile machine, CancellationToken cancellationToken)
    {
        var analysis = SupportEstimator.Analyse(mesh, settings.SupportAngleDegrees, settings.LayerHeightMm, cancellationToken);
        if (analysis.OverhangArea >= Math.Max(10.0, 0.005 * topology.SurfaceArea))
            issues.Add(new(FeasibilityCode.Overhangs, FeasibilitySeverity.Warning,
                $"Surplombs : environ {analysis.OverhangArea:0} mm² au-dessus du vide (plus de {settings.SupportAngleDegrees:0}° par rapport à la verticale). Des supports sont nécessaires, de l'ordre de {analysis.ColumnVolume * machine.SupportDensity / 1000:0.##} cm³.",
                analysis.OverhangArea, Pick(analysis.Points, settings.MaxHighlightPoints)));
        return (analysis.OverhangArea, analysis.ColumnVolume);
    }

    private static void CheckBase(List<FeasibilityIssue> issues, PrintMesh mesh, FeasibilitySettings settings, CancellationToken cancellationToken)
    {
        var sections = LayerSlicer.Slice(mesh, settings.LayerHeightMm, cancellationToken);
        if (sections.Length == 0) return;
        var contact = Math.Abs(sections[0].Area);
        if (contact < 20)
            issues.Add(new(FeasibilityCode.SmallBase, FeasibilitySeverity.Warning,
                $"La surface posée sur le plateau est très petite ({contact:0.#} mm²) : la pièce risque de se décoller. Une bordure (brim) ou une autre orientation aide.", contact));
        else if (mesh.Size.Z / Math.Sqrt(contact) > 6)
            issues.Add(new(FeasibilityCode.SmallBase, FeasibilitySeverity.Warning,
                $"La pièce est haute ({Mm(mesh.Size.Z)}) pour sa base ({contact:0} mm²) : elle risque de se décoller ou de vibrer pendant l'impression.", contact));
    }

    private static FeasibilityReport Report(List<FeasibilityIssue> issues, Vector3 size, int shells, double? minWall, double thinArea, double overhangArea, double supportVolume)
    {
        var verdict = issues.Any(issue => issue.Severity == FeasibilitySeverity.Error) ? FeasibilityVerdict.NotPrintable
            : issues.Any(issue => issue.Severity == FeasibilitySeverity.Warning) ? FeasibilityVerdict.PrintableWithWarnings
            : FeasibilityVerdict.Printable;
        return new FeasibilityReport(verdict, issues, size, shells, minWall, thinArea, overhangArea, supportVolume);
    }

    /// <summary>At most <paramref name="count"/> points, evenly taken from the list.</summary>
    private static IReadOnlyList<Vector3> Pick(IReadOnlyList<Vector3> points, int count)
    {
        if (points.Count <= count) return points.ToArray();
        var picked = new Vector3[count];
        for (var i = 0; i < count; i++) picked[i] = points[(int)((long)i * points.Count / count)];
        return picked;
    }

    private static string Mm(double value) => value.ToString("0.##", French) + " mm";

    private static PrintMesh Flipped(PrintMesh mesh)
    {
        var indices = (int[])mesh.Indices.Clone();
        for (var t = 0; t + 2 < indices.Length; t += 3) (indices[t + 1], indices[t + 2]) = (indices[t + 2], indices[t + 1]);
        return new PrintMesh(mesh.Positions, indices);
    }
}
