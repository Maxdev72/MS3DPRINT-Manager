namespace MS3DPRINT.Manager.Core.Files;

public sealed record ProjectFileEntry(string Name, string FullPath, bool IsDirectory, long? Length, DateTimeOffset LastWriteTime)
{
    public string Icon => IsDirectory ? "📁" : "📄";
    public string TypeLabel => IsDirectory ? "Dossier" : Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 } extension ? extension : "Fichier";
    public string SizeLabel => Length is not { } bytes ? "—" : bytes < 1024 ? $"{bytes} o"
        : bytes < 1024 * 1024 ? (bytes / 1024d).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " Ko"
        : bytes < 1024L * 1024 * 1024 ? (bytes / (1024d * 1024)).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " Mo"
        : (bytes / (1024d * 1024 * 1024)).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " Go";
}
