using System.Numerics;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class ModelOrientationTests
{
    private static MeshModel Cube(float size = 10)
    {
        Vector3[] corners = [new(0, 0, 0), new(size, 0, 0), new(size, size, 0), new(0, size, 0), new(0, 0, size), new(size, 0, size), new(size, size, size), new(0, size, size)];
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        return new MeshModel(corners, faces);
    }

    [Fact]
    public void A_new_orientation_is_the_identity_and_still_stands_the_part_on_the_plate()
    {
        var orientation = new ModelOrientation();
        var shifted = new MeshModel(Cube().Positions.Select(p => p + new Vector3(50, -30, 7)).ToArray(), Cube().Indices);

        var placed = orientation.Apply(shifted);

        Assert.True(orientation.IsIdentity);
        Assert.Equal(0, placed.Min.Z, 4);
        Assert.Equal(0, (placed.Min.X + placed.Max.X) / 2, 4);
        Assert.Equal(0, (placed.Min.Y + placed.Max.Y) / 2, 4);
    }

    [Fact]
    public void Four_quarter_turns_about_an_axis_come_back_to_the_start()
    {
        var orientation = new ModelOrientation();

        for (var i = 0; i < 4; i++) orientation.RotateAxis(1, 90);

        Assert.True(orientation.IsIdentity);
    }

    [Fact]
    public void A_quarter_turn_about_X_lays_a_standing_part_down()
    {
        var orientation = new ModelOrientation();
        var tall = new MeshModel(Cube().Positions.Select(p => new Vector3(p.X, p.Y, p.Z * 3)).ToArray(), Cube().Indices);

        orientation.RotateAxis(0, 90);
        var lying = orientation.Apply(tall);

        Assert.Equal(10, lying.Size.X, 3);
        Assert.Equal(30, lying.Size.Y, 3);
        Assert.Equal(10, lying.Size.Z, 3);
    }

    [Fact]
    public void FaceDown_puts_the_chosen_face_on_the_plate()
    {
        var orientation = new ModelOrientation();

        orientation.FaceDown(Vector3.UnitX);
        var placed = orientation.Apply(Cube());

        // the corners that had x = 10 (indices 1, 2, 5, 6) are the face that now lies on the plate
        foreach (var corner in new[] { 1, 2, 5, 6 }) Assert.Equal(0, placed.Positions[corner].Z, 4);
        Assert.All(placed.Positions, point => Assert.True(point.Z > -1e-4f));
    }

    [Fact]
    public void FaceDown_takes_the_normal_in_the_coordinates_of_the_part_as_shown()
    {
        var orientation = new ModelOrientation();
        orientation.RotateAxis(1, 90);          // the original +X face now looks up (or down)
        var shown = orientation.Apply(Cube());
        var upFace = shown.Positions[5].Z > shown.Positions[0].Z ? Vector3.UnitZ : -Vector3.UnitZ;

        orientation.FaceDown(upFace);            // lay that same face on the plate
        var flipped = orientation.Apply(Cube());

        Assert.Equal(0, flipped.Min.Z, 4);
        Assert.Equal(10, flipped.Size.Z, 3);
    }

    [Fact]
    public void FaceDown_ignores_a_normal_that_has_no_direction()
    {
        var orientation = new ModelOrientation();

        orientation.FaceDown(Vector3.Zero);
        orientation.FaceDown(new Vector3(float.NaN, 0, 0));

        Assert.True(orientation.IsIdentity);
        Assert.Equal(0, orientation.Apply(Cube()).Min.Z, 4);
    }

    [Theory]
    [InlineData(10, 20, 30)]
    [InlineData(-45, 60, 170)]
    [InlineData(0, 0, 0)]
    [InlineData(90, -30, -90)]
    public void Euler_angles_come_back_as_they_were_set(double x, double y, double z)
    {
        var orientation = new ModelOrientation();

        orientation.SetEuler(x, y, z);
        var angles = orientation.EulerDegrees;

        Assert.Equal(x, angles.X, 2);
        Assert.Equal(y, angles.Y, 2);
        Assert.Equal(z, angles.Z, 2);
    }

    [Fact]
    public void Euler_angles_stay_consistent_where_the_second_axis_locks()
    {
        var orientation = new ModelOrientation();
        orientation.SetEuler(20, 90, 35);
        var before = orientation.Rotation;

        var angles = orientation.EulerDegrees;
        orientation.SetEuler(angles.X, angles.Y, angles.Z);

        Assert.True(MathF.Abs(Quaternion.Dot(before, orientation.Rotation)) > 0.9999f);
    }

    [Fact]
    public void Apply_keeps_every_triangle_and_never_touches_the_original()
    {
        var orientation = new ModelOrientation();
        orientation.SetEuler(33, 21, 77);
        var original = Cube();
        var corner = original.Positions[6];

        var placed = orientation.Apply(original);

        Assert.Same(original.Indices, placed.Indices);
        Assert.Equal(corner, original.Positions[6]);
        Assert.Equal(0, placed.Min.Z, 4);
    }

    [Fact]
    public void Reset_goes_back_to_the_loaded_orientation()
    {
        var orientation = new ModelOrientation();
        orientation.SetEuler(10, 20, 30);

        orientation.Reset();

        Assert.True(orientation.IsIdentity);
    }

    [Fact]
    public void An_unknown_axis_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelOrientation().RotateAxis(3, 90));

    private static void AssertSame(MeshModel expected, MeshModel actual)
    {
        Assert.Equal(expected.Positions.Length, actual.Positions.Length);
        for (var i = 0; i < expected.Positions.Length; i++) Assert.True((expected.Positions[i] - actual.Positions[i]).Length() < 1e-3f);
    }

    [Fact]
    public void A_second_identical_mesh_lands_on_the_placed_first_one_without_turning()
    {
        var orientation = new ModelOrientation();
        var first = new MeshModel(Cube(20).Positions.Select(p => p + new Vector3(50, -30, 7)).ToArray(), Cube(20).Indices);

        AssertSame(orientation.Apply(first), orientation.ApplyLike(first, first));
    }

    [Fact]
    public void A_second_identical_mesh_lands_on_the_placed_first_one_when_turned()
    {
        var orientation = new ModelOrientation();
        orientation.RotateAxis(0, 90);
        orientation.RotateAxis(2, 33);
        var first = new MeshModel(Cube(20).Positions.Select(p => p * new Vector3(1, 2, 3) + new Vector3(50, -30, 7)).ToArray(), Cube(20).Indices);

        AssertSame(orientation.Apply(first), orientation.ApplyLike(first, first));
    }

    [Fact]
    public void A_second_mesh_shifted_in_the_file_stays_shifted_by_the_same_amount()
    {
        var orientation = new ModelOrientation();
        var first = Cube(20);
        var second = new MeshModel(first.Positions.Select(p => p + new Vector3(5, 0, 0)).ToArray(), first.Indices);

        var placedFirst = orientation.Apply(first);
        var placedSecond = orientation.ApplyLike(first, second);

        for (var i = 0; i < first.Positions.Length; i++)
            Assert.True((placedSecond.Positions[i] - placedFirst.Positions[i] - new Vector3(5, 0, 0)).Length() < 1e-3f);
    }
}
