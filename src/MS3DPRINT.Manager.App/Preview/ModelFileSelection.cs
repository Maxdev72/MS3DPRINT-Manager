using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.App.Preview;

public static class ModelFileSelection
{
    public static string Select(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var selected = paths.ToArray();
        if (selected.Length != 1 || string.IsNullOrWhiteSpace(selected[0]))
            throw new ArgumentException("Sélectionnez un seul fichier 3D.", nameof(paths));
        var path = Path.GetFullPath(selected[0]);
        if (!ThreeDFileSupport.IsPreviewable(path))
            throw new NotSupportedException("Le visualiseur accepte les fichiers STL et OBJ.");
        if (!File.Exists(path)) throw new FileNotFoundException("Le fichier 3D est introuvable.", path);
        return path;
    }
}
