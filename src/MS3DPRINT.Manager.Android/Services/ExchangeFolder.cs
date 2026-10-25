using Avalonia.Platform.Storage;

namespace MS3DPRINT.Manager.Companion.Services;

/// <summary>The "Echanges mobile" folder of the Nextcloud workspace, reached through the Android file picker (no wide storage permission).</summary>
public sealed class ExchangeFolder
{
    private IStorageFolder? _folder;
    public bool IsConfigured => _folder is not null;
    public string? Name { get; private set; }

    /// <summary>Reopens the folder chosen earlier. False when it was never chosen or is no longer reachable.</summary>
    public async Task<bool> RestoreAsync(IStorageProvider storage, LocalData data)
    {
        _folder = null;
        if (string.IsNullOrEmpty(data.FolderBookmark)) return false;
        try
        {
            _folder = await storage.OpenFolderBookmarkAsync(data.FolderBookmark);
            Name = _folder is null ? null : data.FolderName ?? _folder.Name;
            return _folder is not null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or Java.Lang.Exception)
        { _folder = null; return false; }
    }

    public async Task<bool> PickAsync(IStorageProvider storage, LocalData data)
    {
        var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choisir le dossier « Echanges mobile »", AllowMultiple = false });
        if (picked.FirstOrDefault() is not { } folder) return false;
        _folder = folder; Name = folder.Name;
        data.FolderBookmark = await folder.SaveBookmarkAsync(); data.FolderName = folder.Name;
        return true;
    }

    public async Task<IReadOnlyList<string>> ListNamesAsync()
    {
        var names = new List<string>();
        if (_folder is null) return names;
        await foreach (var item in _folder.GetItemsAsync()) names.Add(item.Name);
        return names;
    }

    /// <summary>Writes a file unless a file of that name is already there (a retry after a partial send must not duplicate the photo).</summary>
    public async Task WriteAsync(string name, byte[] bytes, IReadOnlyCollection<string>? existing = null)
    {
        var folder = _folder ?? throw new IOException("Le dossier d’échange n’est pas choisi.");
        existing ??= await ListNamesAsync();
        if (existing.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        var file = await folder.CreateFileAsync(name) ?? throw new IOException("Le dossier d’échange refuse la création du fichier.");
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    /// <summary>Creates then deletes a tiny file: proves that the phone may write in the folder.</summary>
    public async Task TestWriteAsync()
    {
        var folder = _folder ?? throw new IOException("Le dossier d’échange n’est pas choisi.");
        var file = await folder.CreateFileAsync("test-connexion-" + Guid.NewGuid().ToString("N")[..6] + ".tmp") ?? throw new IOException("Le dossier d’échange refuse la création du fichier.");
        await using (var stream = await file.OpenWriteAsync()) await stream.WriteAsync("ok"u8.ToArray());
        await file.DeleteAsync();
    }

    public async Task<byte[]?> ReadAsync(string name)
    {
        if (_folder is null) return null;
        await foreach (var item in _folder.GetItemsAsync())
        {
            if (!string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase) || item is not IStorageFile file) continue;
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            return memory.ToArray();
        }
        return null;
    }
}
