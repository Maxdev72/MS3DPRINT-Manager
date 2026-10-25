using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Short, interruptible native transitions; displayed data is never interpolated or hidden.</summary>
internal static class BrandMotion
{
    public static void Enter(Control control, bool reduced, double distance = 12)
    {
        control.Transitions = null;
        control.Opacity = 1;
        control.RenderTransform = TransformOperations.Parse("translate(0px,0px)");
        if (reduced) return;
        control.RenderTransform = TransformOperations.Parse(FormattableString.Invariant($"translateX({distance}px)"));
        control.Transitions = Movement(180);
        control.RenderTransform = TransformOperations.Parse("translate(0px,0px)");
    }

    public static void Confirm(Control control, bool reduced)
    {
        control.Transitions = null;
        control.RenderTransform = TransformOperations.Parse("scale(1)");
        if (reduced) return;
        control.RenderTransformOrigin = new Avalonia.RelativePoint(0, .5, Avalonia.RelativeUnit.Relative);
        control.RenderTransform = TransformOperations.Parse("scale(1.015)");
        control.Transitions = Movement(140);
        control.RenderTransform = TransformOperations.Parse("scale(1)");
    }

    /// <summary>A short reveal anchored to the control that opened the overlay. Never delays focus.</summary>
    public static void OpenOverlay(Control control, bool reduced)
    {
        control.Transitions = null;
        control.RenderTransform = TransformOperations.Parse("translateY(0px)");
        control.Opacity = 1;
        if (reduced) return;
        control.RenderTransform = TransformOperations.Parse("translateY(-6px)");
        control.Opacity = .92;
        control.Transitions = Movement(160);
        control.Transitions.Add(new DoubleTransition { Property = Avalonia.Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) });
        control.RenderTransform = TransformOperations.Parse("translateY(0px)");
        control.Opacity = 1;
    }

    public static void DimBackground(Control control, bool reduced)
    {
        control.Transitions = null;
        control.Opacity = reduced ? 1 : 0;
        if (reduced) return;
        control.Transitions = new Transitions
        {
            new DoubleTransition { Property = Avalonia.Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new CubicEaseOut() }
        };
        control.Opacity = 1;
    }

    public static Transitions Movement(int milliseconds) => new()
    {
        new TransformOperationsTransition { Property = Avalonia.Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(milliseconds), Easing = new CubicEaseOut() }
    };
}
