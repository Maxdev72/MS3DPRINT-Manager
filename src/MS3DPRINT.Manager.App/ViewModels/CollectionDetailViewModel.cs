using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class CollectionDetailViewModel : ObservableObject
{
    private readonly CollectionItemSummary _item;
    private readonly ProjectFileBrowser _files;

    public CollectionDetailViewModel(CollectionItemSummary item, ProjectFileBrowser? files = null)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _files = files ?? new ProjectFileBrowser();
    }

    public string Name => _item.Name;
    public bool HasProfile => _item.Profile is not null;
    public string? Description => _item.Profile?.Description;
    public string? Contact => _item.Profile?.Contact;
    public string? Address => _item.Profile?.Address;
    public string? Phone => _item.Profile?.Phone;
    public string? Email => _item.Profile?.Email;
    public string? Website => _item.Profile?.Website;
    public string? Notes => _item.Profile?.Notes;
    public bool IsSupplier => _item.Path.Split(Path.DirectorySeparatorChar).Contains("06_FOURNISSEURS", StringComparer.OrdinalIgnoreCase);
    public string RootPath => _item.Path;
    public string CurrentDirectory { get; private set; } = string.Empty;
    public string CurrentPathLabel
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CurrentDirectory)) return Name;
            var relative = Path.GetRelativePath(RootPath, CurrentDirectory);
            return relative == "." ? Name : Name + " › " + relative.Replace(Path.DirectorySeparatorChar.ToString(), " › ").Replace(Path.AltDirectorySeparatorChar.ToString(), " › ");
        }
    }
    public IReadOnlyList<ProjectFileEntry> FileEntries { get; private set; } = [];
    public bool CanGoUp => !string.IsNullOrWhiteSpace(CurrentDirectory) && !string.Equals(RootPath, CurrentDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public CollectionFileListing ReadFiles()
    {
        var current = string.IsNullOrWhiteSpace(CurrentDirectory) ? RootPath : CurrentDirectory;
        try
        {
            return new CollectionFileListing(current, _files.List(RootPath, current));
        }
        catch (DirectoryNotFoundException) when (!string.Equals(RootPath, current, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && Directory.Exists(RootPath))
        {
            return new CollectionFileListing(RootPath, _files.List(RootPath, RootPath));
        }
    }

    public CollectionFileListing ReadFilesForDirectory(ProjectFileEntry entry)
    {
        if (!entry.IsDirectory) throw new ArgumentException("L’entrée sélectionnée n’est pas un dossier.", nameof(entry));
        return new CollectionFileListing(entry.FullPath, _files.List(RootPath, entry.FullPath));
    }

    public CollectionFileListing ReadParentFiles()
    {
        if (!CanGoUp) return new CollectionFileListing(CurrentDirectory, FileEntries);
        var parent = _files.GetParentDirectory(RootPath, CurrentDirectory);
        return new CollectionFileListing(parent, _files.List(RootPath, parent));
    }

    public void ApplyFileListing(CollectionFileListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);
        CurrentDirectory = listing.DirectoryPath;
        FileEntries = listing.Entries;
        OnPropertyChanged(nameof(CurrentDirectory));
        OnPropertyChanged(nameof(CurrentPathLabel));
        OnPropertyChanged(nameof(FileEntries));
        OnPropertyChanged(nameof(CanGoUp));
    }

    public void ApplyFolderSizes(IReadOnlyDictionary<string, long?> sizes)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        FileEntries = FileEntries.Select(entry => entry.IsDirectory && sizes.TryGetValue(entry.FullPath, out var size)
            ? entry with { Length = size }
            : entry).ToArray();
        OnPropertyChanged(nameof(FileEntries));
    }
}

public sealed record CollectionFileListing(string DirectoryPath, IReadOnlyList<ProjectFileEntry> Entries);
