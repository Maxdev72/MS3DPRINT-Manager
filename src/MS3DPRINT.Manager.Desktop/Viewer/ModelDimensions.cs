using System.Globalization;
using System.Numerics;

namespace MS3DPRINT.Manager.Desktop.Viewer;

public readonly record struct ModelDimensions(double X, double Y, double Z)
{
    public string ToDisplayText(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return $"Dimensions : X {X.ToString("G4", culture)} × Y {Y.ToString("G4", culture)} × Z {Z.ToString("G4", culture)} (unités du modèle)";
    }

    public static ModelDimensions FromSize(Vector3 size)
    {
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || !float.IsFinite(size.Z))
            throw new InvalidDataException("Le modèle 3D ne contient aucune géométrie mesurable.");
        return new ModelDimensions(size.X, size.Y, size.Z);
    }

    public static ModelDimensions FromMesh(MeshModel mesh) => FromSize(mesh.Size);
}
