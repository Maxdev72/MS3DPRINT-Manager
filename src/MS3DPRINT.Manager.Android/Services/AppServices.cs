using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Mobile;

namespace MS3DPRINT.Manager.Companion.Services;

/// <summary>
/// The phone's working state: what it remembers, the exchange folder, the offline queue and the picture of the workshop published by the PC.
/// Anything noted is first saved on the phone and then sent: a missing network or folder never loses an entry.
/// </summary>
public sealed class AppServices
{
    public static AppServices Current { get; } = new();

    private readonly SemaphoreSlim _sending = new(1, 1);
    public LocalStore Store { get; } = new();
    public LocalData Data { get; private set; } = new();
    public ExchangeFolder Exchange { get; } = new();
    public PcSnapshot? Snapshot { get; private set; }
    public string? SnapshotProblem { get; private set; }
    public event Action? Changed;

    public int PendingCount => Data.History.Count(h => h.Status != "Sent");

    public void RaiseChanged() => Changed?.Invoke();

    public async Task InitializeAsync(IStorageProvider storage)
    {
        Data = Store.Load();
        LoadCachedSnapshot();
        await Exchange.RestoreAsync(storage, Data);
        RaiseChanged();
        await RefreshSnapshotAsync();
        await SendPendingAsync();
    }

    private void LoadCachedSnapshot()
    {
        try { Snapshot = File.Exists(Store.SnapshotPath) ? PcSnapshotFile.Parse(File.ReadAllBytes(Store.SnapshotPath)) : null; }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { Snapshot = null; }
    }

    /// <summary>Reads the file the PC published. On failure the previous copy stays in use, so the screens still work away from the network.</summary>
    public async Task<string> RefreshSnapshotAsync()
    {
        if (!Exchange.IsConfigured) { SnapshotProblem = "Dossier d’échange non choisi."; RaiseChanged(); return SnapshotProblem; }
        try
        {
            var bytes = await Exchange.ReadAsync(PcSnapshot.FileName);
            if (bytes is null) { SnapshotProblem = "Le PC n’a pas encore publié l’état de l’atelier (ouvrez MS3DPRINT Manager sur le PC)."; RaiseChanged(); return SnapshotProblem; }
            var snapshot = PcSnapshotFile.Parse(bytes);
            File.WriteAllBytes(Store.SnapshotPath, bytes);
            Snapshot = snapshot; SnapshotProblem = null; Data.SnapshotReadAt = DateTimeOffset.Now; Store.Save(Data);
            RaiseChanged();
            return "État de l’atelier mis à jour.";
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or UnauthorizedAccessException or InvalidOperationException or Java.Lang.Exception)
        {
            SnapshotProblem = "Lecture de l’état du PC impossible : " + exception.Message; RaiseChanged(); return SnapshotProblem;
        }
    }

    /// <summary>Saves the entry on the phone first, then tries to send it.</summary>
    public async Task<HistoryItem> EnqueueAsync(HistoryItem item, byte[] json, byte[]? attachment)
    {
        Directory.CreateDirectory(Store.OutboxDirectory);
        await File.WriteAllBytesAsync(Path.Combine(Store.OutboxDirectory, item.BaseName + ".json"), json);
        if (attachment is not null) await File.WriteAllBytesAsync(Path.Combine(Store.OutboxDirectory, item.BaseName + (item.AttachmentExt ?? ".jpg")), attachment);
        item.Status = "Pending"; item.At = DateTimeOffset.Now; item.HasPhoto = attachment is not null;
        if (attachment is null) item.AttachmentExt = null;
        Data.History.Insert(0, item);
        if (Data.History.Count > 120) Data.History.RemoveRange(120, Data.History.Count - 120);
        Store.Save(Data); RaiseChanged();
        await SendPendingAsync();
        return item;
    }

    /// <summary>Sends everything still waiting, oldest first. A failure keeps the entry and its photo for the next try.</summary>
    public async Task<int> SendPendingAsync()
    {
        if (!await _sending.WaitAsync(0)) return 0;
        var sent = 0;
        try
        {
            if (!Exchange.IsConfigured) return 0;
            var waiting = Data.History.Where(h => h.Status != "Sent").OrderBy(h => h.At).ToList();
            if (waiting.Count == 0) return 0;
            IReadOnlyCollection<string> existing;
            try { existing = await Exchange.ListNamesAsync(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Java.Lang.Exception)
            { foreach (var h in waiting) { h.Status = "Failed"; h.Error = Friendly(exception); } Store.Save(Data); RaiseChanged(); return 0; }
            foreach (var item in waiting)
            {
                var data = Path.Combine(Store.OutboxDirectory, item.BaseName + ".json");
                var extension = item.AttachmentExt ?? (item.HasPhoto ? ".jpg" : null);
                var photo = Path.Combine(Store.OutboxDirectory, item.BaseName + (extension ?? ".jpg"));
                try
                {
                    if (!File.Exists(data)) throw new IOException("Le fichier en attente a disparu du téléphone.");
                    // The photo goes first: the PC then never finds data whose photo has not arrived.
                    if (extension is not null && File.Exists(photo)) await Exchange.WriteAsync(item.BaseName + extension, await File.ReadAllBytesAsync(photo), existing);
                    await Exchange.WriteAsync(item.BaseName + ".json", await File.ReadAllBytesAsync(data), existing);
                    item.Status = "Sent"; item.Error = null; item.At = DateTimeOffset.Now; sent++;
                    File.Delete(data); if (File.Exists(photo)) File.Delete(photo);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Java.Lang.Exception)
                { item.Status = "Failed"; item.Error = Friendly(exception); }
                Store.Save(Data); RaiseChanged();
            }
        }
        finally { _sending.Release(); }
        return sent;
    }

    /// <summary>The Android messages are technical ("Missing file for primary:..."): say what to do instead.</summary>
    public static string Friendly(Exception exception)
    {
        var text = exception.Message ?? "";
        if (exception is FileNotFoundException or DirectoryNotFoundException || text.Contains("Missing file", StringComparison.OrdinalIgnoreCase) || text.Contains("No such file", StringComparison.OrdinalIgnoreCase) || text.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return "Le dossier d’échange est introuvable (déplacé, renommé ou pas encore synchronisé). Il repartira tout seul dès qu’il sera de nouveau là, ou choisissez-le à nouveau dans Réglages.";
        if (exception is UnauthorizedAccessException || text.Contains("Permission", StringComparison.OrdinalIgnoreCase) || text.Contains("denied", StringComparison.OrdinalIgnoreCase))
            return "L’accès au dossier d’échange a été retiré. Choisissez-le à nouveau dans Réglages.";
        return text;
    }

    public void ClearSent()
    {
        Data.History.RemoveAll(h => h.Status == "Sent");
        Store.Save(Data); RaiseChanged();
    }

    public void Discard(HistoryItem item)
    {
        foreach (var extension in new[] { ".json", ".jpg", ".pdf" })
        {
            var path = Path.Combine(Store.OutboxDirectory, item.BaseName + extension);
            if (File.Exists(path)) File.Delete(path);
        }
        Data.History.Remove(item); Store.Save(Data); RaiseChanged();
    }
}
