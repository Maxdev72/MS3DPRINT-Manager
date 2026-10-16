using System.Numerics;
using MS3DPRINT.Estimator;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>
/// How the model was turned on the plate: one rotation applied to the model as loaded. <see cref="Apply"/> gives the part as shown,
/// turned and stood on the plate (see <see cref="PrintMesh.Oriented"/>).
/// </summary>
public sealed class ModelOrientation
{
    public Quaternion Rotation { get; private set; } = Quaternion.Identity;

    public bool IsIdentity => MathF.Abs(MathF.Abs(Rotation.W) - 1) < 1e-6f;

    public void Reset() => Rotation = Quaternion.Identity;

    /// <summary>Turns the part about an axis of the plate (0 = X, 1 = Y, 2 = Z) after its current orientation.</summary>
    public void RotateAxis(int axis, double degrees)
    {
        var direction = axis switch
        {
            0 => Vector3.UnitX,
            1 => Vector3.UnitY,
            2 => Vector3.UnitZ,
            _ => throw new ArgumentOutOfRangeException(nameof(axis), "L'axe attendu est 0 (X), 1 (Y) ou 2 (Z).")
        };
        Compose(Quaternion.CreateFromAxisAngle(direction, (float)(degrees * Math.PI / 180)));
    }

    /// <summary>Applies <paramref name="turn"/> after the current orientation, in the coordinates of the part as shown.</summary>
    public void Compose(Quaternion turn) => Rotation = Quaternion.Normalize(Quaternion.Concatenate(Rotation, turn));

    /// <summary>
    /// Lays the face whose outward normal is <paramref name="normal"/> (in the coordinates of the part as shown) on the plate.
    /// A normal with no direction (zero length or not finite) changes nothing.
    /// </summary>
    public void FaceDown(Vector3 normal)
    {
        var length = normal.Length();
        if (!float.IsFinite(length) || length < 1e-6f) return;
        Compose(OrientationMath.Between(normal, -Vector3.UnitZ));
    }

    /// <summary>
    /// The rotation as three angles in degrees, turning about X first, then Y, then Z (axes of the plate). Where the second angle is ±90°
    /// the first and third cannot be told apart: the third is then 0.
    /// </summary>
    public Vector3 EulerDegrees
    {
        get
        {
            var m = Matrix4x4.CreateFromQuaternion(Rotation);
            var sinB = Math.Clamp(-m.M13, -1f, 1f);
            var b = MathF.Asin(sinB);
            float a, c;
            if (MathF.Abs(MathF.Cos(b)) < 1e-4f)
            {
                a = MathF.Atan2(-m.M32, m.M22);
                c = 0;
            }
            else
            {
                a = MathF.Atan2(m.M23, m.M33);
                c = MathF.Atan2(m.M12, m.M11);
            }
            const float ToDegrees = 180f / MathF.PI;
            return new Vector3(a * ToDegrees, b * ToDegrees, c * ToDegrees);
        }
    }

    public void SetEuler(double x, double y, double z)
    {
        const double ToRadians = Math.PI / 180;
        var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)(x * ToRadians));
        var qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(y * ToRadians));
        var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(z * ToRadians));
        Rotation = Quaternion.Normalize(Quaternion.Concatenate(Quaternion.Concatenate(qx, qy), qz));
    }

    /// <summary>The part as shown: the loaded model turned, centred on the plate in X and Y and standing on it.</summary>
    public MeshModel Apply(MeshModel original)
    {
        var placed = new PrintMesh(original.Positions, original.Indices).Oriented(Rotation);
        return new MeshModel(placed.Positions, placed.Indices);
    }

    /// <summary>
    /// Places <paramref name="other"/> exactly as <see cref="Apply"/> placed <paramref name="reference"/>: the same turn about the centre of
    /// the reference, then the same shift, so two versions saved in one frame of reference stay in it when compared.
    /// </summary>
    public MeshModel ApplyLike(MeshModel reference, MeshModel other)
    {
        var placedReference = Apply(reference);
        var centre = reference.Center;
        var shift = placedReference.Positions[0] - Vector3.Transform(reference.Positions[0] - centre, Rotation);
        var moved = new Vector3[other.Positions.Length];
        for (var i = 0; i < moved.Length; i++) moved[i] = Vector3.Transform(other.Positions[i] - centre, Rotation) + shift;
        return new MeshModel(moved, other.Indices);
    }
}
