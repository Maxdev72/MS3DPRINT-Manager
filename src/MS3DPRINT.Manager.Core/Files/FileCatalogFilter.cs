namespace MS3DPRINT.Manager.Core.Files;

public enum FileFamily { All, Models, Cad, Pdf, Images, Other }

public static class FileCatalogFilter
{
    public static FileFamily Family(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".stl" or ".3mf" or ".obj" => FileFamily.Models,
        ".step" or ".stp" or ".iges" or ".igs" or ".fcstd" or ".sldprt" or ".sldasm" or ".f3d" or ".f3z" or ".3dm" => FileFamily.Cad,
        ".pdf" => FileFamily.Pdf,
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp" => FileFamily.Images,
        _ => FileFamily.Other
    };
    public static string Label(FileFamily family) => family switch
    {
        FileFamily.Models => "Modèles 3D", FileFamily.Cad => "CAO", FileFamily.Pdf => "PDF",
        FileFamily.Images => "Images", FileFamily.Other => "Autres fichiers", _ => "Tous les formats"
    };
    public static IReadOnlyList<ProjectFileEntry> Filter(IReadOnlyList<ProjectFileEntry> entries, string? query, FileFamily family)
    {
        var terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var comparer = System.Globalization.CultureInfo.GetCultureInfo("fr-FR").CompareInfo;
        return entries.Where(entry => (entry.IsDirectory || family == FileFamily.All || Family(entry.Name) == family) &&
            terms.All(term => comparer.IndexOf(entry.Name, term, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0)).ToArray();
    }
}
