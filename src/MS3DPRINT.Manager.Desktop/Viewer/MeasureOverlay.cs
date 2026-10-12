using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Draws the measured segments, their end points and their lengths over the 3D view.</summary>
public sealed class MeasureOverlay : Control
{
    public MeasureOverlay() { IsHitTestVisible = false; }

    public MeshModel? Mesh { get; set; }
    public ViewCamera? Camera { get; set; }
    public IReadOnlyList<(Vector3 A, Vector3 B)> Segments { get; set; } = [];
    public Vector3? Pending { get; set; }
    /// <summary>Millimetres per model unit.</summary>
    public double Scale { get; set; } = 1;

    public static string FormatLength(float units, double scale) =>
        (units * scale).ToString("0.##", CultureInfo.GetCultureInfo("fr-FR")) + " mm";

    public override void Render(DrawingContext context)
    {
        if (Mesh is not { } mesh || Camera is not { } camera || Bounds.Width < 2 || Bounds.Height < 2) return;
        var accent = (IBrush?)Application.Current?.FindResource("FocusBorderBrush") ?? Brushes.White;
        var chip = (IBrush?)Application.Current?.FindResource("RaisedSurfaceBrush") ?? Brushes.Black;
        var text = (IBrush?)Application.Current?.FindResource("TextBrush") ?? Brushes.White;
        var pen = new Pen(accent, 2);
        var halo = new Pen(Brushes.Black, 4) { LineCap = PenLineCap.Round };

        bool Project(Vector3 point, out Point screen)
        {
            var ok = ViewMath.TryProject(mesh, camera, Bounds.Width, Bounds.Height, point, out var x, out var y);
            screen = new Point(x, y);
            return ok;
        }

        void Dot(Point at)
        {
            context.DrawEllipse(Brushes.Black, null, at, 6, 6);
            context.DrawEllipse(accent, null, at, 4, 4);
        }

        foreach (var (a, b) in Segments)
        {
            if (!Project(a, out var from) || !Project(b, out var to)) continue;
            context.DrawLine(halo, from, to);
            context.DrawLine(pen, from, to);
            Dot(from);
            Dot(to);
            var label = new FormattedText(FormatLength(Vector3.Distance(a, b), Scale), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 13, text) { };
            var middle = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
            var box = new Rect(middle.X - label.Width / 2 - 7, middle.Y - label.Height - 14, label.Width + 14, label.Height + 8);
            context.DrawRectangle(chip, new Pen(accent, 1), box, 6, 6);
            context.DrawText(label, new Point(box.X + 7, box.Y + 4));
        }

        if (Pending is { } pending && Project(pending, out var start)) Dot(start);
    }
}
