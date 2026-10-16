using System.Numerics;

namespace MS3DPRINT.Estimator;

public enum SupportMode { None, Auto }

/// <summary>What the customer chooses: the few parameters that change weight and time.</summary>
public sealed record EstimateSettings
{
    public double LayerHeightMm { get; init; } = 0.2;
    public int WallCount { get; init; } = 2;
    public double InfillPercent { get; init; } = 15;
    /// <summary>Thickness printed solid under and over the model.</summary>
    public double TopBottomThicknessMm { get; init; } = 0.8;
    public SupportMode Supports { get; init; } = SupportMode.None;
    /// <summary>Faces leaning further than this from the vertical need support (60° is the 30° from the horizontal of Bambu Studio).</summary>
    public double SupportAngleDegrees { get; init; } = 60;
    /// <summary>Material density in g/cm³.</summary>
    public double DensityGcm3 { get; init; } = 1.24;
    /// <summary>
    /// What the material lets the hotend melt per second (mm³/s): the datasheet "maximum volumetric speed". It is what makes PETG,
    /// ABS or TPU slower than PLA on the same machine.
    /// </summary>
    public double MaterialMaxFlowMm3S { get; init; } = 20;

    internal void Validate()
    {
        if (!(LayerHeightMm is >= 0.04 and <= 1.0)) throw new ArgumentOutOfRangeException(nameof(LayerHeightMm), "Hauteur de couche attendue entre 0,04 et 1 mm.");
        if (WallCount is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(WallCount), "Nombre de parois attendu entre 1 et 20.");
        if (!(InfillPercent is >= 0 and <= 100)) throw new ArgumentOutOfRangeException(nameof(InfillPercent), "Remplissage attendu entre 0 et 100 %.");
        if (!(TopBottomThicknessMm is >= 0 and <= 20)) throw new ArgumentOutOfRangeException(nameof(TopBottomThicknessMm));
        if (!(SupportAngleDegrees is > 0 and < 90)) throw new ArgumentOutOfRangeException(nameof(SupportAngleDegrees));
        if (!(DensityGcm3 is > 0 and <= 10)) throw new ArgumentOutOfRangeException(nameof(DensityGcm3));
        if (!(MaterialMaxFlowMm3S is > 0 and <= 100)) throw new ArgumentOutOfRangeException(nameof(MaterialMaxFlowMm3S), "Débit maximal attendu entre 0 et 100 mm³/s.");
    }
}

/// <summary>
/// An average printer. The constants were fitted on measurements of five printers (Bambu Lab A1 mini, A1, P1S, X1 Carbon, H2S)
/// at several layer heights, two materials and with and without supports, see tools/EstimatorBench: they describe the typical
/// machine, not one of them. What differs from a printer to another is only how fast its head really moves, hence
/// <see cref="HeadSpeedFactor"/>; what differs between materials is <see cref="EstimateSettings.MaterialMaxFlowMm3S"/>.
/// </summary>
public sealed record MachineProfile
{
    public double NozzleDiameterMm { get; init; } = 0.4;
    public double FilamentDiameterMm { get; init; } = 1.75;
    /// <summary>
    /// Speed class of the printer compared with the average: 1 for the average, about 1.35 for a fast CoreXY with high accelerations
    /// (P1S, X1C, H2S), about 0.6 for a printer that is kept slow (A1 family at its default quality profiles). It scales how fast the
    /// head moves; it does not change what the material can melt per second.
    /// </summary>
    public double HeadSpeedFactor { get; init; } = 1.0;
    /// <summary>Effective speeds for a 0.2 mm layer, accelerations and corners included, not the speeds written in a slicer profile.</summary>
    public double WallSpeedMmS { get; init; } = 75;
    public double SkinSpeedMmS { get; init; } = 80;
    public double InfillSpeedMmS { get; init; } = 170;
    public double SupportSpeedMmS { get; init; } = 43;
    /// <summary>
    /// Thinner layers do not slow the head as much as the volume alone suggests: the time of a feature follows the volume divided by
    /// the layer height to this power (1 would be a pure volume model).
    /// </summary>
    public double LayerHeightExponent { get; init; } = 0.76;
    /// <summary>What the hotend itself can melt per second, whatever the material; the material usually limits first.</summary>
    public double MaxFlowMm3S { get; init; } = 40;
    /// <summary>Support filling, as a fraction of the volume it holds up. Measured against Bambu Studio's tree supports, which waste more than a plain column.</summary>
    public double SupportDensity { get; init; } = 0.45;
    /// <summary>Travel moves, retractions and layer change, per layer.</summary>
    public double LayerOverheadSeconds { get; init; } = 2.4;
    /// <summary>The head brakes in corners: time lost per radian turned by a wall (a full circle is 2π).</summary>
    public double TurnSeconds { get; init; } = 0.0242;
    /// <summary>Overhangs and bridges are printed slowly: time per mm² of section that appears above empty air.</summary>
    public double OverhangSecondsPerMm2 { get; init; } = 0.1205;
    /// <summary>Scales every duration on top of everything else, to fit a printer that is slower or faster than described.</summary>
    public double TimeFactor { get; init; } = 1.0;
    public double WeightFactor { get; init; } = 1.0;
    /// <summary>How much of the geometric volume the slicer really lays down: neighbouring lines overlap and sparse infill stops short of the walls.</summary>
    public double WallFlowFactor { get; init; } = 0.98;
    public double SkinFlowFactor { get; init; } = 0.95;
    public double InfillFlowFactor { get; init; } = 0.84;

    public double ExtrusionWidthMm => NozzleDiameterMm * 1.05;

    internal void Validate()
    {
        if (!(NozzleDiameterMm is >= 0.1 and <= 2)) throw new ArgumentOutOfRangeException(nameof(NozzleDiameterMm));
        if (!(FilamentDiameterMm is > 0 and <= 5)) throw new ArgumentOutOfRangeException(nameof(FilamentDiameterMm));
        foreach (var value in new[] { HeadSpeedFactor, WallSpeedMmS, SkinSpeedMmS, InfillSpeedMmS, SupportSpeedMmS, MaxFlowMm3S, TimeFactor, WeightFactor, WallFlowFactor, SkinFlowFactor, InfillFlowFactor })
            if (!(value > 0)) throw new ArgumentOutOfRangeException(nameof(MachineProfile), "Les vitesses et les facteurs doivent être positifs.");
        if (!(LayerHeightExponent is > 0 and <= 1.5) || !(SupportDensity is >= 0 and <= 1) || LayerOverheadSeconds < 0 || TurnSeconds < 0 || OverhangSecondsPerMm2 < 0)
            throw new ArgumentOutOfRangeException(nameof(MachineProfile));
    }
}

public enum EstimateWarning
{
    /// <summary>The mesh has holes: the slice volume and the surface volume disagree, so the figures are unreliable.</summary>
    OpenMesh,
    /// <summary>The triangles were wound inside out; the estimate was made as if they were not.</summary>
    InvertedMesh,
    /// <summary>The model has no height or no volume.</summary>
    EmptyModel
}

public sealed record EstimateResult(
    double WeightGrams, double FilamentLengthMm, double TimeSeconds, int LayerCount,
    double WallVolumeMm3, double SkinVolumeMm3, double InfillVolumeMm3, double SupportVolumeMm3,
    double ModelVolumeMm3, Vector3 SizeMm, IReadOnlyList<EstimateWarning> Warnings,
    double ContourLengthMm = 0, int IslandCount = 0, double TurningRadians = 0, double OverhangAreaMm2 = 0, double BaseAreaMm2 = 0)
{
    public double PrintedVolumeMm3 => WallVolumeMm3 + SkinVolumeMm3 + InfillVolumeMm3 + SupportVolumeMm3;
}
