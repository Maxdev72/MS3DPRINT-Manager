using System.Numerics;

namespace MS3DPRINT.Estimator;

public enum AutoOrientGoal { FewestSupports, Fastest, MostStable }

/// <summary>One way to stand the part and what it costs. The rotation is relative to the part that was given.</summary>
public sealed record OrientationCandidate(
    Quaternion Rotation, double SupportVolumeMm3, double TimeSeconds, double BaseAreaMm2, double HeightMm, bool FitsPlate, double Score);

public sealed record OrientationProposal(OrientationCandidate Best, OrientationCandidate Current, IReadOnlyList<OrientationCandidate> Candidates, bool Limited)
{
    public bool IsImprovement => !ReferenceEquals(Best, Current);
}

/// <summary>
/// Looks for the way to stand a part that suits the goal best. Candidates are the six directions of the axes and the largest families of
/// flat faces (normals within 5° of each other); each is laid on the bed and measured with the estimator.
/// </summary>
public static class AutoOrient
{
    public const int MaxFaceFamilies = 12;
    public const double FamilyAngleDegrees = 5;
    /// <summary>Above this many triangles only the six axes are tried: every candidate costs a full estimate.</summary>
    public const int LargeMeshTriangles = 1_000_000;
    private const double SmallBaseMm2 = 20;
    /// <summary>
    /// Scores closer than this are a tie: estimates of equivalent positions of a symmetric part differ by a percent or so, most likely
    /// from the layer-count rounding in LayerSlicer (a float-noisy height can add a nearly empty layer).
    /// </summary>
    private const double TieMargin = 0.02;

    public static OrientationProposal Propose(
        PrintMesh mesh, EstimateSettings settings, MachineProfile? machine, AutoOrientGoal goal, Vector3? plateMm,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(settings);
        var limited = mesh.TriangleCount > LargeMeshTriangles;

        // outward normals that will point down; the first one is the position the part is in now
        var normals = new List<Vector3> { -Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY };
        if (!limited)
            foreach (var family in FaceFamilies(mesh, cancellationToken))
                if (normals.All(known => Vector3.Dot(known, family) < 0.99985f)) normals.Add(family);

        var measuring = settings with { Supports = SupportMode.Auto };
        var raw = new List<(Quaternion Rotation, double Support, double Time, double Base, double Height, bool Fits)>();
        for (var i = 0; i < normals.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rotation = OrientationMath.Between(normals[i], -Vector3.UnitZ);
            var placed = mesh.Oriented(rotation);
            // a part that fits the plate only after a quarter turn about Z gets that turn in its rotation, so the rotation returned fits as drawn
            if (plateMm is { } plate && !FitsAsIs(placed.Size, plate) && FitsAsIs(new Vector3(placed.Size.Y, placed.Size.X, placed.Size.Z), plate))
            {
                rotation = Quaternion.Concatenate(rotation, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2));
                placed = mesh.Oriented(rotation);
            }
            try
            {
                var estimate = PrintEstimator.Estimate(placed, measuring, machine, cancellationToken);
                raw.Add((rotation, estimate.SupportVolumeMm3, estimate.TimeSeconds, estimate.BaseAreaMm2, placed.Size.Z, Fits(placed.Size, plateMm)));
            }
            catch (ArgumentException) when (i > 0)
            {
                // a position the estimator refuses (far too many layers) is simply not a candidate
            }
            progress?.Report((i + 1.0) / normals.Count);
        }

        var maxSupport = Math.Max(1, raw.Max(r => r.Support));
        var maxTime = Math.Max(1, raw.Max(r => r.Time));
        var maxBase = Math.Max(1, raw.Max(r => r.Base));
        var (weightSupport, weightTime, weightBase) = goal switch
        {
            AutoOrientGoal.Fastest => (0.25, 1.0, 0.1),
            AutoOrientGoal.MostStable => (0.25, 0.25, 1.0),
            _ => (1.0, 0.25, 0.1)
        };
        var candidates = raw.Select(r => new OrientationCandidate(
            r.Rotation, r.Support, r.Time, r.Base, r.Height, r.Fits,
            weightSupport * r.Support / maxSupport + weightTime * r.Time / maxTime + weightBase * (1 - r.Base / maxBase)
            + (r.Base < SmallBaseMm2 ? 0.5 : 0) + (r.Fits ? 0 : 100))).ToList();

        var current = candidates[0];
        var best = candidates.MinBy(candidate => candidate.Score)!;
        // a tie keeps the part where it is
        if (best.Score >= current.Score - TieMargin) best = current;
        return new OrientationProposal(best, current, candidates, limited);
    }

    /// <summary>The part fits the plate as it stands (no turn about Z; the candidate's rotation already holds any turn).</summary>
    private static bool Fits(Vector3 size, Vector3? plate) => plate is not { } p || FitsAsIs(size, p);

    private static bool FitsAsIs(Vector3 size, Vector3 p) =>
        size.X <= p.X + 0.01f && size.Y <= p.Y + 0.01f && size.Z <= p.Z + 0.01f;

    /// <summary>Outward normals of the largest flat regions of the mesh, largest first.</summary>
    private static List<Vector3> FaceFamilies(PrintMesh mesh, CancellationToken cancellationToken)
    {
        var count = mesh.TriangleCount;
        var normals = new Vector3[count];
        var areas = new float[count];
        for (var t = 0; t < count; t++)
        {
            if ((t & 0x3FFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = mesh.Positions[mesh.Indices[t * 3]];
            var b = mesh.Positions[mesh.Indices[t * 3 + 1]];
            var c = mesh.Positions[mesh.Indices[t * 3 + 2]];
            var cross = Vector3.Cross(b - a, c - a);
            var length = cross.Length();
            areas[t] = length / 2;
            normals[t] = length > 1e-12f ? cross / length : Vector3.Zero;
        }

        var limit = (float)Math.Cos(FamilyAngleDegrees * Math.PI / 180);
        var taken = new bool[count];
        var families = new List<(Vector3 Normal, double Area)>();
        for (var round = 0; round < MaxFaceFamilies * 2; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seed = -1;
            var largest = 0f;
            for (var t = 0; t < count; t++)
                if (!taken[t] && areas[t] > largest) { largest = areas[t]; seed = t; }
            if (seed < 0) break;
            var axis = normals[seed];
            double total = 0;
            for (var t = 0; t < count; t++)
                if (!taken[t] && Vector3.Dot(normals[t], axis) >= limit) { taken[t] = true; total += areas[t]; }
            families.Add((axis, total));
        }
        return families.OrderByDescending(family => family.Area).Take(MaxFaceFamilies).Select(family => family.Normal).ToList();
    }
}
