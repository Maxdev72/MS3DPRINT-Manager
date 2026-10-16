using System.Buffers.Binary;
using System.Text;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Writes a mesh as a binary STL, in millimetres (STL has no unit, so the unit of the model is converted on the way out).</summary>
public static class StlWriter
{
    private const int HeaderBytes = 80;
    private const int TriangleBytes = 50;

    public static void Write(string path, MeshModel mesh, double millimetresPerUnit = 1)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (!(millimetresPerUnit > 0) || !double.IsFinite(millimetresPerUnit)) throw new ArgumentOutOfRangeException(nameof(millimetresPerUnit));
        var scale = (float)millimetresPerUnit;
        var count = mesh.TriangleCount;
        var bytes = new byte[HeaderBytes + 4 + count * TriangleBytes];
        Encoding.ASCII.GetBytes("MS3DPRINT Manager").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(HeaderBytes), (uint)count);
        var offset = HeaderBytes + 4;
        for (var t = 0; t < count; t++)
        {
            var normal = mesh.Normals[t];
            WriteVector(bytes, offset, normal.X, normal.Y, normal.Z);
            for (var corner = 0; corner < 3; corner++)
            {
                var point = mesh.Positions[mesh.Indices[t * 3 + corner]] * scale;
                WriteVector(bytes, offset + 12 + corner * 12, point.X, point.Y, point.Z);
            }
            offset += TriangleBytes;
        }
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteVector(byte[] bytes, int offset, float x, float y, float z)
    {
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), x);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 8), z);
    }
}
