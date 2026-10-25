using System.Text.Json;

namespace MS3DPRINT.Manager.Companion.Services;

/// <summary>One line of the "Envois" screen: what the user noted, and whether it already reached the exchange folder.</summary>
public sealed class HistoryItem
{
    public Guid Id { get; set; }
    /// <summary>"achat" or "recette".</summary>
    public string Kind { get; set; } = "achat";
    public string BaseName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public bool HasPhoto { get; set; }
    /// <summary>".jpg" for a photo, ".pdf" for a shared invoice; null when nothing is attached.</summary>
    public string? AttachmentExt { get; set; }
    public DateTimeOffset At { get; set; }
    /// <summary>"Pending", "Sent" or "Failed".</summary>
    public string Status { get; set; } = "Pending";
    public string? Error { get; set; }
    public string? ProjectRef { get; set; }
}

/// <summary>The form as the user left it, so that a closed app (Android does it while the camera is open) never loses the typing.</summary>
public sealed class EntryDraft
{
    public int Mode { get; set; }
    public int Nature { get; set; }
    public string Supplier { get; set; } = "";
    public string Client { get; set; } = "";
    public string Label { get; set; } = "";
    public string Amount { get; set; } = "";
    public string Goods { get; set; } = "";
    public string Payment { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Invoice { get; set; } = "";
    public string Project { get; set; } = "";
    public DateTime? Date { get; set; }

    public bool IsEmpty => Supplier.Length + Client.Length + Label.Length + Amount.Length + Goods.Length + Reference.Length + Invoice.Length + Project.Length == 0;
}

/// <summary>Everything the phone remembers between two launches.</summary>
public sealed class LocalData
{
    public string? FolderBookmark { get; set; }
    public string? FolderName { get; set; }
    public string DefaultPayment { get; set; } = "Carte bancaire";
    public List<HistoryItem> History { get; set; } = [];
    public List<string> Suppliers { get; set; } = [];
    public List<string> Clients { get; set; } = [];
    public DateTimeOffset? SnapshotReadAt { get; set; }
    public EntryDraft? Draft { get; set; }
}

public sealed class LocalStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _directory;
    private readonly object _gate = new();

    public LocalStore(string? directory = null)
    {
        _directory = directory ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(OutboxDirectory);
    }

    public string OutboxDirectory => Path.Combine(_directory, "outbox");
    private string DataPath => Path.Combine(_directory, "compagnon.json");
    public string SnapshotPath => Path.Combine(_directory, "etat-pc.json");

    public LocalData Load()
    {
        lock (_gate)
        {
            try { return File.Exists(DataPath) ? JsonSerializer.Deserialize<LocalData>(File.ReadAllText(DataPath)) ?? new() : new(); }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { return new(); }
        }
    }

    /// <summary>Written to a temporary file first so that a crash never leaves a half-written state.</summary>
    public void Save(LocalData data)
    {
        lock (_gate)
        {
            try
            {
                var temporary = DataPath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(data, Options));
                File.Move(temporary, DataPath, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    public static void Remember(List<string> list, string value, int max = 40)
    {
        value = value.Trim();
        if (value.Length == 0) return;
        list.RemoveAll(item => string.Equals(item, value, StringComparison.CurrentCultureIgnoreCase));
        list.Insert(0, value);
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
    }
}
