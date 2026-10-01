using System.Windows.Media.Media3D;
using GpuMesh = HelixToolkit.SharpDX.MeshGeometry3D;

namespace MS3DPRINT.Manager.App.Preview;

public sealed record GpuPreviewScene(IReadOnlyList<GpuMesh> Parts, Rect3D Bounds, int TriangleCount);
