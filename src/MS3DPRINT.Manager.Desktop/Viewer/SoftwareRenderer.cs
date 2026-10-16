using System.Numerics;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Orbit camera around a mesh. Z is the model's up axis, as in slicers.</summary>
public sealed class ViewCamera
{
    public double Yaw { get; set; } = -0.6;
    public double Pitch { get; set; } = 0.45;
    public double Zoom { get; set; } = 1;
    /// <summary>Screen-space pan in pixels at full resolution.</summary>
    public double PanX { get; set; }
    public double PanY { get; set; }

    /// <summary>Practically no limit: a millionth of the model to a billion times its size, only there to keep the arithmetic finite.</summary>
    public const double MinZoom = 1e-6;
    public const double MaxZoom = 1e9;

    /// <summary>
    /// Zooms by a factor around a point of the view: the spot under the cursor stays under the cursor, so the user can dive into any
    /// detail of the model or back out without ever reaching a limit. The pan is in the same units as the other camera methods.
    /// </summary>
    public void ZoomAt(double factor, double cursorX, double cursorY, double width, double height)
    {
        if (!double.IsFinite(factor) || factor <= 0) return;
        var next = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        var applied = next / Zoom;
        var scale = height / 760.0;
        var offsetX = cursorX - width / 2;
        var offsetY = cursorY - height / 2;
        PanX = (offsetX - (offsetX - PanX * scale) * applied) / scale;
        PanY = (offsetY - (offsetY - PanY * scale) * applied) / scale;
        Zoom = next;
    }

    public void Reset()
    {
        Yaw = -0.6; Pitch = 0.45; Zoom = 1; PanX = 0; PanY = 0;
    }
}

public readonly record struct RenderOptions(int Width, int Height, bool Wireframe, uint BaseColor, uint BackgroundColor, ClipPlane? Clip = null);

/// <summary>
/// CPU rasteriser with a z-buffer and flat shading. It needs no GPU driver, so the viewer behaves the same
/// on every machine; rows are split into bands that are filled in parallel.
/// </summary>
public static class SoftwareRenderer
{
    private const double FieldOfView = 40 * Math.PI / 180;
    private const int MaxWireTriangles = 200_000;

    public static uint[] Render(MeshModel mesh, ViewCamera camera, RenderOptions options,
        IReadOnlyList<Vector3>? markers = null, CancellationToken cancellationToken = default)
    {
        var width = Math.Max(1, options.Width);
        var height = Math.Max(1, options.Height);
        var pixels = new uint[width * height];
        Array.Fill(pixels, options.BackgroundColor);

        var projection = Project(mesh, camera, width, height);
        cancellationToken.ThrowIfCancellationRequested();

        if (options.Wireframe) DrawWireframe(mesh, projection, pixels, width, height, options.BaseColor, options.Clip, cancellationToken);
        else FillTriangles(mesh, camera, projection, pixels, width, height, options.BaseColor, options.Clip, cancellationToken);

        if (markers is not null)
            foreach (var marker in markers)
                if (projection.TryProject(marker, out var x, out var y, out _))
                    DrawDot(pixels, width, height, (int)x, (int)y, 3, 0xFFFF6A00);
        return pixels;
    }

    /// <summary>Projects a model point to the screen; used for the axis gizmo and markers.</summary>
    public static bool TryProjectPoint(MeshModel mesh, ViewCamera camera, int width, int height, Vector3 point, out double x, out double y)
    {
        var projection = Project(mesh, camera, width, height);
        return projection.TryProject(point, out x, out y, out _);
    }

    private sealed class Projection
    {
        public required float[] ScreenX { get; init; }
        public required float[] ScreenY { get; init; }
        public required float[] InverseDepth { get; init; }
        public required Vector3 Center { get; init; }
        public required Matrix4x4 Rotation { get; init; }
        public required double Distance { get; init; }
        public required double Focal { get; init; }
        public required double CenterX { get; init; }
        public required double CenterY { get; init; }

        public bool TryProject(Vector3 point, out double x, out double y, out double depth)
        {
            var view = Vector3.Transform(point - Center, Rotation);
            depth = view.Y + Distance;
            if (depth <= 1e-6) { x = y = 0; return false; }
            x = CenterX + view.X * Focal / depth;
            y = CenterY - view.Z * Focal / depth;
            return true;
        }
    }

    /// <summary>Screen-space direction (x right, y down) of a model axis, for the axis gizmo.</summary>
    public static (double X, double Y) ProjectDirection(ViewCamera camera, Vector3 direction)
    {
        var view = Vector3.Transform(direction, BuildRotation(camera));
        return (view.X, -view.Z);
    }

    private static Matrix4x4 BuildRotation(ViewCamera camera) =>
        Matrix4x4.CreateRotationZ((float)camera.Yaw) * Matrix4x4.CreateRotationX((float)camera.Pitch);

    private static Projection Project(MeshModel mesh, ViewCamera camera, int width, int height)
    {
        var rotation = BuildRotation(camera);
        var center = mesh.Center;
        var distance = mesh.Radius * 2.9 / Math.Max(ViewCamera.MinZoom, camera.Zoom);
        var focal = height * 0.5 / Math.Tan(FieldOfView / 2);
        var scale = height / 760.0; // pan is expressed for a 760 px high view
        var cx = width / 2.0 + camera.PanX * scale;
        var cy = height / 2.0 + camera.PanY * scale;
        var count = mesh.Positions.Length;
        var screenX = new float[count];
        var screenY = new float[count];
        var inverseDepth = new float[count];
        var positions = mesh.Positions;
        Parallel.For(0, (count + 4095) / 4096, block =>
        {
            var end = Math.Min(count, (block + 1) * 4096);
            for (var i = block * 4096; i < end; i++)
            {
                var view = Vector3.Transform(positions[i] - center, rotation);
                var depth = view.Y + distance;
                if (depth <= 1e-6)
                {
                    inverseDepth[i] = 0; // behind the camera: triangles using it are skipped
                    continue;
                }
                inverseDepth[i] = (float)(1 / depth);
                screenX[i] = (float)(cx + view.X * focal / depth);
                screenY[i] = (float)(cy - view.Z * focal / depth);
            }
        });
        return new Projection
        {
            ScreenX = screenX, ScreenY = screenY, InverseDepth = inverseDepth, Center = center, Rotation = rotation,
            Distance = distance, Focal = focal, CenterX = cx, CenterY = cy
        };
    }

    private static void FillTriangles(MeshModel mesh, ViewCamera camera, Projection projection, uint[] pixels,
        int width, int height, uint baseColor, ClipPlane? clip, CancellationToken cancellationToken)
    {
        var indices = mesh.Indices;
        var triangleCount = mesh.TriangleCount;
        var rotation = BuildRotation(camera);
        var light = Vector3.Normalize(new Vector3(-0.35f, -1f, 0.55f));
        var shades = new byte[triangleCount];
        var minRow = new int[triangleCount];
        var maxRow = new int[triangleCount];
        var sx = projection.ScreenX; var sy = projection.ScreenY; var iz = projection.InverseDepth;

        // Pre-pass: cull, shade, and compute each triangle's pixel rows.
        Parallel.For(0, (triangleCount + 4095) / 4096, block =>
        {
            var end = Math.Min(triangleCount, (block + 1) * 4096);
            for (var t = block * 4096; t < end; t++)
            {
                var a = indices[t * 3]; var b = indices[t * 3 + 1]; var c = indices[t * 3 + 2];
                var normal = mesh.Normals[t];
                if (iz[a] <= 0 || iz[b] <= 0 || iz[c] <= 0 || normal == Vector3.Zero || IsCut(mesh, clip, a, b, c)) { maxRow[t] = -1; continue; }
                var top = MathF.Min(sy[a], MathF.Min(sy[b], sy[c]));
                var bottom = MathF.Max(sy[a], MathF.Max(sy[b], sy[c]));
                var left = MathF.Min(sx[a], MathF.Min(sx[b], sx[c]));
                var right = MathF.Max(sx[a], MathF.Max(sx[b], sx[c]));
                if (bottom < 0 || top >= height || right < 0 || left >= width || !float.IsFinite(top + bottom + left + right)) { maxRow[t] = -1; continue; }
                minRow[t] = Math.Max(0, (int)MathF.Floor(top));
                maxRow[t] = Math.Min(height - 1, (int)MathF.Ceiling(bottom));
                var viewNormal = Vector3.Transform(normal, rotation);
                shades[t] = (byte)(255 * (0.28f + 0.72f * MathF.Abs(Vector3.Dot(viewNormal, light))));
            }
        });
        cancellationToken.ThrowIfCancellationRequested();

        var depthBuffer = new float[width * height];
        var bands = Math.Max(1, Math.Min(Environment.ProcessorCount * 2, height / 8));
        var baseR = (baseColor >> 16) & 0xFF; var baseG = (baseColor >> 8) & 0xFF; var baseB = baseColor & 0xFF;
        Parallel.For(0, bands, band =>
        {
            var rowStart = band * height / bands;
            var rowEnd = (band + 1) * height / bands - 1;
            for (var t = 0; t < triangleCount; t++)
            {
                if (maxRow[t] < rowStart || minRow[t] > rowEnd || maxRow[t] < 0) continue;
                var a = indices[t * 3]; var b = indices[t * 3 + 1]; var c = indices[t * 3 + 2];
                var x0 = sx[a]; var y0 = sy[a]; var x1 = sx[b]; var y1 = sy[b]; var x2 = sx[c]; var y2 = sy[c];
                var area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
                if (MathF.Abs(area) < 1e-6f) continue;
                var invArea = 1 / area;
                var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(x0, MathF.Min(x1, x2))));
                var maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(x0, MathF.Max(x1, x2))));
                var yFrom = Math.Max(rowStart, minRow[t]);
                var yTo = Math.Min(rowEnd, maxRow[t]);
                var shade = shades[t] / 255f;
                var color = 0xFF000000u | ((uint)(baseR * shade) << 16) | ((uint)(baseG * shade) << 8) | (uint)(baseB * shade);
                for (var y = yFrom; y <= yTo; y++)
                {
                    var py = y + 0.5f;
                    for (var x = minX; x <= maxX; x++)
                    {
                        var px = x + 0.5f;
                        var w0 = ((x1 - px) * (y2 - py) - (x2 - px) * (y1 - py)) * invArea;
                        var w1 = ((x2 - px) * (y0 - py) - (x0 - px) * (y2 - py)) * invArea;
                        var w2 = 1 - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                        var depth = w0 * iz[a] + w1 * iz[b] + w2 * iz[c];
                        var at = y * width + x;
                        if (depth <= depthBuffer[at]) continue;
                        depthBuffer[at] = depth;
                        pixels[at] = color;
                    }
                }
            }
        });
    }

    private static void DrawWireframe(MeshModel mesh, Projection projection, uint[] pixels, int width, int height,
        uint baseColor, ClipPlane? clip, CancellationToken cancellationToken)
    {
        var indices = mesh.Indices;
        var triangleCount = mesh.TriangleCount;
        var step = Math.Max(1, triangleCount / MaxWireTriangles);
        var color = 0xFF000000u | (baseColor & 0xFFFFFF);
        var sx = projection.ScreenX; var sy = projection.ScreenY; var iz = projection.InverseDepth;
        for (var t = 0; t < triangleCount; t += step)
        {
            if ((t & 8191) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = indices[t * 3]; var b = indices[t * 3 + 1]; var c = indices[t * 3 + 2];
            if (iz[a] <= 0 || iz[b] <= 0 || iz[c] <= 0 || IsCut(mesh, clip, a, b, c)) continue;
            DrawLine(pixels, width, height, sx[a], sy[a], sx[b], sy[b], color);
            DrawLine(pixels, width, height, sx[b], sy[b], sx[c], sy[c], color);
            DrawLine(pixels, width, height, sx[c], sy[c], sx[a], sy[a], color);
        }
    }

    /// <summary>A triangle is cut away when its centre lies on the removed side of the plane (no cap is drawn on the CPU view).</summary>
    private static bool IsCut(MeshModel mesh, ClipPlane? clip, int a, int b, int c)
        => clip is { } plane && plane.Clips((mesh.Positions[a] + mesh.Positions[b] + mesh.Positions[c]) / 3f);

    private static void DrawLine(uint[] pixels, int width, int height, float x0, float y0, float x1, float y1, uint color)
    {
        if (!float.IsFinite(x0 + y0 + x1 + y1)) return;
        if (MathF.Max(x0, x1) < 0 || MathF.Min(x0, x1) >= width || MathF.Max(y0, y1) < 0 || MathF.Min(y0, y1) >= height) return;
        var dx = x1 - x0; var dy = y1 - y0;
        var steps = (int)MathF.Min(MathF.Max(MathF.Abs(dx), MathF.Abs(dy)), 4096);
        if (steps == 0) steps = 1;
        for (var i = 0; i <= steps; i++)
        {
            var x = (int)(x0 + dx * i / steps); var y = (int)(y0 + dy * i / steps);
            if ((uint)x < (uint)width && (uint)y < (uint)height) pixels[y * width + x] = color;
        }
    }

    private static void DrawDot(uint[] pixels, int width, int height, int cx, int cy, int radius, uint color)
    {
        for (var y = -radius; y <= radius; y++)
            for (var x = -radius; x <= radius; x++)
            {
                if (x * x + y * y > radius * radius) continue;
                var px = cx + x; var py = cy + y;
                if ((uint)px < (uint)width && (uint)py < (uint)height) pixels[py * width + px] = color;
            }
    }
}
