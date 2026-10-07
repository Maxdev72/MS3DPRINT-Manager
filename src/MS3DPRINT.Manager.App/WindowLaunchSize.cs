using System.Windows;

namespace MS3DPRINT.Manager.App;

public static class WindowLaunchSize
{
    private const double MainPreferredWidth = 1080;
    private const double MainPreferredHeight = 700;
    private const double MainMaximumWidth = 1200;
    private const double MainMaximumHeight = 850;
    private const double MainMinimumWidth = 390;
    private const double MainMinimumHeight = 500;
    private const double WorkAreaUsage = 0.90;

    public static Size ForMainWindow(Size workArea, Size? savedSize = null)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea), "La zone de travail doit avoir une largeur et une hauteur positives.");

        var maximumWidth = Math.Min(MainMaximumWidth, Math.Floor(workArea.Width * WorkAreaUsage));
        var maximumHeight = Math.Min(MainMaximumHeight, Math.Floor(workArea.Height * WorkAreaUsage));
        if (savedSize is { } saved && saved.Width > 0 && saved.Height > 0)
        {
            return new Size(
                Math.Min(maximumWidth, Math.Max(MainMinimumWidth, saved.Width)),
                Math.Min(maximumHeight, Math.Max(MainMinimumHeight, saved.Height)));
        }

        return new Size(
            Math.Min(MainPreferredWidth, maximumWidth),
            Math.Min(MainPreferredHeight, maximumHeight));
    }
}
