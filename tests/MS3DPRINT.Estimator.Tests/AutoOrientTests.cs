using System.Numerics;
using MS3DPRINT.Estimator;

namespace MS3DPRINT.Estimator.Tests;

public sealed class AutoOrientTests
{
    private static PrintMesh Block(float x, float y, float z)
    {
        Vector3[] corners = [new(0, 0, 0), new(x, 0, 0), new(x, y, 0), new(0, y, 0), new(0, 0, z), new(x, 0, z), new(x, y, z), new(0, y, z)];
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        return new PrintMesh(corners, faces).Oriented(Quaternion.Identity);
    }

    /// <summary>A closed solid of revolution around Z from a (radius, height) profile, wound outward whatever the profile direction.</summary>
    private static PrintMesh Revolve((double R, double Z)[] profile, int segments = 32)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        for (var ring = 0; ring < segments; ring++)
            foreach (var (r, z) in profile)
            {
                var angle = 2 * Math.PI * ring / segments;
                positions.Add(new Vector3((float)(r * Math.Cos(angle)), (float)(r * Math.Sin(angle)), (float)z));
            }
        var n = profile.Length;
        void Add(int a, int b, int c)
        {
            if (Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).LengthSquared() < 1e-9f) return;
            indices.AddRange([a, b, c]);
        }
        for (var ring = 0; ring < segments; ring++)
        {
            var next = (ring + 1) % segments;
            for (var k = 0; k < n; k++)
            {
                var l = (k + 1) % n;
                int a = ring * n + k, b = next * n + k, c = next * n + l, d = ring * n + l;
                Add(a, b, c);
                Add(a, c, d);
            }
        }
        var volume = 0.0;
        for (var t = 0; t < indices.Count; t += 3)
            volume += Vector3.Dot(positions[indices[t]], Vector3.Cross(positions[indices[t + 1]], positions[indices[t + 2]])) / 6.0;
        if (volume < 0)
            for (var t = 0; t < indices.Count; t += 3) (indices[t + 1], indices[t + 2]) = (indices[t + 2], indices[t + 1]);
        return new PrintMesh([.. positions], [.. indices]).Oriented(Quaternion.Identity);
    }

    private static EstimateSettings Settings() => new() { InfillPercent = 15 };

    [Fact]
    public void A_cube_is_left_where_it_is()
    {
        var proposal = AutoOrient.Propose(Block(20, 20, 20), Settings(), null, AutoOrientGoal.FewestSupports, null, null, default);

        Assert.False(proposal.IsImprovement);
        Assert.Same(proposal.Current, proposal.Best);
    }

    [Fact]
    public void A_plate_standing_on_its_edge_is_laid_flat()
    {
        var standing = Block(2, 40, 60);

        var proposal = AutoOrient.Propose(standing, Settings(), null, AutoOrientGoal.FewestSupports, null, null, default);
        var placed = standing.Oriented(proposal.Best.Rotation);

        Assert.True(proposal.IsImprovement);
        Assert.Equal(2, placed.Size.Z, 2);
    }

    [Fact]
    public void A_mushroom_standing_on_its_stem_is_turned_cap_down()
    {
        // stem 3 mm in radius and 20 mm high, then a cap 15 mm in radius: the cap hangs in the air
        var mushroom = Revolve([(0, 0), (3, 0), (3, 20), (15, 20), (15, 24), (0, 24)]);

        var proposal = AutoOrient.Propose(mushroom, Settings(), null, AutoOrientGoal.FewestSupports, null, null, default);

        Assert.True(proposal.IsImprovement);
        Assert.True(proposal.Current.SupportVolumeMm3 > 100);
        Assert.True(proposal.Best.SupportVolumeMm3 < 0.2 * proposal.Current.SupportVolumeMm3);
    }

    [Fact]
    public void A_pillar_that_is_too_tall_for_the_plate_is_laid_down_to_fit()
    {
        var pillar = Block(20, 20, 60);

        var proposal = AutoOrient.Propose(pillar, Settings(), null, AutoOrientGoal.FewestSupports, new Vector3(100, 100, 30), null, default);
        var placed = pillar.Oriented(proposal.Best.Rotation);

        Assert.True(proposal.Best.FitsPlate);
        Assert.False(proposal.Current.FitsPlate);
        Assert.True(placed.Size.Z <= 30.01f);
    }

    [Fact]
    public void Candidates_include_the_current_position_first()
    {
        var proposal = AutoOrient.Propose(Block(20, 30, 40), Settings(), null, AutoOrientGoal.FewestSupports, null, null, default);

        Assert.Same(proposal.Current, proposal.Candidates[0]);
        Assert.True(proposal.Candidates.Count >= 6);
    }

    [Fact]
    public void Progress_is_reported_up_to_the_end()
    {
        var reports = new List<double>();

        AutoOrient.Propose(Block(20, 30, 40), Settings(), null, AutoOrientGoal.Fastest, null, new SynchronousProgress(reports.Add), default);

        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports[^1], 6);
    }

    [Fact]
    public void A_cancelled_search_stops()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            AutoOrient.Propose(Block(20, 30, 40), Settings(), null, AutoOrientGoal.FewestSupports, null, null, cancelled.Token));
    }

    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
