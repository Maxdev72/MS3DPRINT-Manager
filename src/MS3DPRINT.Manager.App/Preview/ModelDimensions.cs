using System.Windows.Media.Media3D;
using System.Globalization;

namespace MS3DPRINT.Manager.App.Preview;

public readonly record struct ModelDimensions(double X, double Y, double Z)
{
    public string ToDisplayText(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return $"Dimensions : X {X.ToString("G4", culture)} × Y {Y.ToString("G4", culture)} × Z {Z.ToString("G4", culture)} (unités du modèle)";
    }

    public static ModelDimensions FromBounds(Rect3D bounds)
    {
        if (bounds.IsEmpty) throw new InvalidDataException("Le modèle 3D ne contient aucune géométrie mesurable.");
        return new ModelDimensions(bounds.SizeX, bounds.SizeY, bounds.SizeZ);
    }
}
