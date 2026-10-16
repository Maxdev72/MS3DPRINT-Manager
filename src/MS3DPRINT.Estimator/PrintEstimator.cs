using System.Numerics;

namespace MS3DPRINT.Estimator;

/// <summary>
/// Estimates weight and print time of a mesh without running a slicer: the model is cut into layers, and each layer's area
/// and contour length give the walls, the solid skins and the sparse infill. No toolpath is built, only lengths and volumes.
/// </summary>
public static class PrintEstimator
{
    private const int MaxLayers = 50_000;

    public static EstimateResult Estimate(PrintMesh mesh, EstimateSettings settings, MachineProfile? machine = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(settings);
        machine ??= new MachineProfile();
        settings.Validate();
        machine.Validate();
        if ((double)mesh.Size.Z / settings.LayerHeightMm > MaxLayers)
            throw new ArgumentException($"Le modèle dépasse {MaxLayers} couches avec cette hauteur de couche.", nameof(mesh));

        var sections = LayerSlicer.Slice(mesh, settings.LayerHeightMm, cancellationToken);
        var warnings = new List<EstimateWarning>();
        var h = settings.LayerHeightMm;
        var height = (double)mesh.Size.Z;

        var signedVolume = 0.0;
        for (var i = 0; i < sections.Length; i++) signedVolume += sections[i].Area * Thickness(i, sections.Length, h, height);
        if (sections.Length == 0 || Math.Abs(signedVolume) < 1e-6)
            return new EstimateResult(0, 0, 0, sections.Length, 0, 0, 0, 0, 0, mesh.Size, [EstimateWarning.EmptyModel]);

        var inverted = signedVolume < 0;
        var area = new double[sections.Length];
        for (var i = 0; i < area.Length; i++) area[i] = inverted ? -sections[i].Area : sections[i].Area;
        if (inverted) warnings.Add(EstimateWarning.InvertedMesh);
        var modelVolume = Math.Abs(signedVolume);
        var surfaceVolume = Math.Abs(SurfaceVolume(mesh));
        if (Math.Abs(surfaceVolume - modelVolume) > 0.05 * Math.Max(surfaceVolume, modelVolume)) warnings.Add(EstimateWarning.OpenMesh);

        var width = machine.ExtrusionWidthMm;
        var skinLayers = settings.TopBottomThicknessMm <= 0 ? 0 : Math.Max(1, (int)Math.Round(settings.TopBottomThicknessMm / h));
        double wallVolume = 0, skinVolume = 0, infillVolume = 0, seconds = 0, turning = 0, overhang = 0;
        var islands = 0;
        for (var i = 0; i < area.Length; i++)
        {
            var thickness = Thickness(i, area.Length, h, height);
            var layer = Analyse(sections[i], inverted, settings.WallCount, width);
            islands += layer.Islands;
            var exposed = Math.Min(layer.Inner, Exposed(area, i, skinLayers, +1) + Exposed(area, i, skinLayers, -1));
            var infillArea = (layer.Inner - exposed) * settings.InfillPercent / 100;

            var wall = layer.Wall * thickness * machine.WallFlowFactor;
            var skin = exposed * thickness * machine.SkinFlowFactor;
            var infill = infillArea * thickness * machine.InfillFlowFactor;
            wallVolume += wall;
            skinVolume += skin;
            infillVolume += infill;

            var layerTurning = layer.Turning * settings.WallCount;
            var layerOverhang = i > 0 ? Math.Max(0, area[i] - area[i - 1]) : 0;
            turning += layerTurning;
            overhang += layerOverhang;
            var extrusion = wall / Rate(machine, settings, machine.WallSpeedMmS, thickness)
                + skin / Rate(machine, settings, machine.SkinSpeedMmS, thickness)
                + infill / Rate(machine, settings, machine.InfillSpeedMmS, thickness);
            // corners and overhangs are kinematic too: a faster head loses less time in them
            var slowdowns = (layerTurning * machine.TurnSeconds + layerOverhang * machine.OverhangSecondsPerMm2) / machine.HeadSpeedFactor;
            seconds += (extrusion + slowdowns) * machine.TimeFactor + machine.LayerOverheadSeconds;
        }

        var supportVolume = 0.0;
        if (settings.Supports == SupportMode.Auto)
        {
            var oriented = inverted ? Flipped(mesh) : mesh;
            supportVolume = SupportEstimator.ColumnVolume(oriented, settings.SupportAngleDegrees, h, cancellationToken) * machine.SupportDensity;
            seconds += supportVolume / Rate(machine, settings, machine.SupportSpeedMmS, h) * machine.TimeFactor;
        }

        var printed = (wallVolume + skinVolume + infillVolume + supportVolume) * machine.WeightFactor;
        var filamentArea = Math.PI * Math.Pow(machine.FilamentDiameterMm / 2, 2);
        return new EstimateResult(printed / 1000 * settings.DensityGcm3, printed / filamentArea, seconds, area.Length,
            wallVolume, skinVolume, infillVolume, supportVolume, modelVolume, mesh.Size, warnings,
            sections.Sum(section => section.Perimeter), islands, turning, overhang);
    }

    /// <summary>The last layer is as thin as what is left of the model height.</summary>
    private static double Thickness(int layer, int count, double layerHeight, double height) =>
        layer == count - 1 ? Math.Max(height - (count - 1) * layerHeight, 1e-6) : layerHeight;

    private const double ReferenceLayerMm = 0.2;

    /// <summary>
    /// Volume the nozzle lays down per second at a head speed (known for a 0.2 mm layer and the average printer), limited by what the
    /// material and the hotend can melt.
    /// </summary>
    private static double Rate(MachineProfile machine, EstimateSettings settings, double speedMmS, double thickness) =>
        Math.Min(machine.ExtrusionWidthMm * speedMmS * machine.HeadSpeedFactor * Math.Pow(ReferenceLayerMm, 1 - machine.LayerHeightExponent) * Math.Pow(thickness, machine.LayerHeightExponent),
            Math.Min(machine.MaxFlowMm3S, settings.MaterialMaxFlowMm3S));

    private const int MaxLoopsForIslands = 2000;

    /// <summary>
    /// Splits a section into its walls and what lies inside them. Every outer contour is an island that takes the holes it
    /// contains, so a thin island is all wall and never gets more wall than it has material.
    /// </summary>
    private static (double Wall, double Inner, int Islands, double Turning) Analyse(LayerSection section, bool inverted, int wallCount, double width)
    {
        var loops = section.Loops;
        var sign = inverted ? -1.0 : 1.0;
        var turning = loops.Sum(loop => loop.Turning);
        if (loops.Length == 0) return (0, 0, 0, 0);
        if (loops.Length > MaxLoopsForIslands)
        {
            var solid = Math.Max(0, sign * section.Area);
            var wall = Math.Min(solid, section.Perimeter * wallCount * width);
            return (wall, solid - wall, loops.Length, turning);
        }

        var outers = new List<int>();
        var holes = new List<int>();
        for (var i = 0; i < loops.Length; i++) (sign * loops[i].Area >= 0 ? outers : holes).Add(i);
        var solidArea = new double[outers.Count];
        var bands = new double[outers.Count];
        for (var o = 0; o < outers.Count; o++)
        {
            solidArea[o] = sign * loops[outers[o]].Area;
            bands[o] = loops[outers[o]].Perimeter * wallCount * width;
        }
        foreach (var h in holes)
        {
            var hole = loops[h];
            var owner = -1;
            for (var o = 0; o < outers.Count; o++)
            {
                var outer = loops[outers[o]];
                if (hole.MinX < outer.MinX || hole.MinY < outer.MinY || hole.MaxX > outer.MaxX || hole.MaxY > outer.MaxY) continue;
                if (owner < 0 || outer.Area * sign < loops[outers[owner]].Area * sign) owner = o;
            }
            if (owner < 0) continue;
            solidArea[owner] += sign * hole.Area;
            bands[owner] += hole.Perimeter * wallCount * width;
        }

        double wallTotal = 0, innerTotal = 0;
        for (var o = 0; o < outers.Count; o++)
        {
            var solid = Math.Max(0, solidArea[o]);
            var wall = Math.Min(solid, bands[o]);
            wallTotal += wall;
            innerTotal += solid - wall;
        }
        return (wallTotal, innerTotal, outers.Count, turning);
    }

    /// <summary>Part of the layer's area not covered by the next <paramref name="count"/> layers in a direction: it faces the air and is printed solid.</summary>
    private static double Exposed(double[] area, int layer, int count, int direction)
    {
        if (count == 0) return 0;
        var covered = double.PositiveInfinity;
        for (var step = 1; step <= count; step++)
        {
            var index = layer + step * direction;
            covered = Math.Min(covered, index >= 0 && index < area.Length ? area[index] : 0);
        }
        return Math.Max(0, area[layer] - covered);
    }

    /// <summary>Volume from the surface alone (signed tetrahedra against the centre); disagrees with the sliced volume when the mesh is open.</summary>
    private static double SurfaceVolume(PrintMesh mesh)
    {
        var origin = (mesh.Min + mesh.Max) / 2;
        var volume = 0.0;
        for (var t = 0; t + 2 < mesh.Indices.Length; t += 3)
        {
            var a = mesh.Positions[mesh.Indices[t]] - origin;
            var b = mesh.Positions[mesh.Indices[t + 1]] - origin;
            var c = mesh.Positions[mesh.Indices[t + 2]] - origin;
            volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
        }
        return volume;
    }

    private static PrintMesh Flipped(PrintMesh mesh)
    {
        var indices = (int[])mesh.Indices.Clone();
        for (var t = 0; t + 2 < indices.Length; t += 3) (indices[t + 1], indices[t + 2]) = (indices[t + 2], indices[t + 1]);
        return new PrintMesh(mesh.Positions, indices);
    }
}
