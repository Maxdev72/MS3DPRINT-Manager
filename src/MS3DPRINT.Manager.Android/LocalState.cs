using System.Text.Json;

namespace MS3DPRINT.Manager.Companion;

/// <summary>What the phone remembers: the exchange folder chosen once, and the latest purchases it sent.</summary>
public sealed class LocalState
{
    public string? FolderBookmark { get; set; }
    public string? FolderName { get; set; }
    public List<SentPurchase> Sent { get; set; } = [];
    public List<string> Suppliers { get; set; } = [];

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "compagnon.json");

    public static LocalState Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<LocalState>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    public void Remember(SentPurchase sent)
    {
        Sent.Insert(0, sent);
        if (Sent.Count > 8) Sent.RemoveRange(8, Sent.Count - 8);
        Suppliers.RemoveAll(s => string.Equals(s, sent.Supplier, StringComparison.CurrentCultureIgnoreCase));
        Suppliers.Insert(0, sent.Supplier);
        if (Suppliers.Count > 40) Suppliers.RemoveRange(40, Suppliers.Count - 40);
        Save();
    }
}

public sealed record SentPurchase(string Supplier, string Label, decimal Amount, DateOnly PurchasedOn, bool HasPhoto);
