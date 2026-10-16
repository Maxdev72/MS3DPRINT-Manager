using System.Numerics;

namespace MS3DPRINT.Estimator;

public static class OrientationMath
{
    /// <summary>The shortest rotation taking the direction <paramref name="from"/> onto <paramref name="to"/> (neither needs to be a unit vector).</summary>
    public static Quaternion Between(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        var dot = Vector3.Dot(from, to);
        if (dot > 0.999999f) return Quaternion.Identity;
        if (dot < -0.999999f)
        {
            // opposite directions: a half turn about any axis at right angles to them
            var axis = Vector3.Cross(from, Vector3.UnitX);
            if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(from, Vector3.UnitY);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        var cross = Vector3.Cross(from, to);
        return Quaternion.Normalize(new Quaternion(cross, 1 + dot));
    }
}
