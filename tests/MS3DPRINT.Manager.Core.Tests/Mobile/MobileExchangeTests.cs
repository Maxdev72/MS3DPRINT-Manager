using System.Text.Json;
using MS3DPRINT.Manager.Core.Mobile;

namespace MS3DPRINT.Manager.Core.Tests.Mobile;

public sealed class MobileExchangeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-mobile-" + Guid.NewGuid().ToString("N"));
    public MobileExchangeTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static MobilePurchase Purchase(string? photo = "x.jpg", string? reference = null, decimal amount = 18.9m) =>
        new(Guid.Parse("11111111-2222-3333-4444-555555555555"), new DateOnly(2026, 10, 25), "Leroy Merlin", "Visserie", amount, "Carte bancaire", reference, "ABC-2026-001", photo, new DateTimeOffset(2026, 10, 25, 14, 0, 0, TimeSpan.Zero));

    private string Drop(MobilePurchase purchase, bool withPhoto = true)
    {
        var folder = MobileInbox.FolderFor(_root); Directory.CreateDirectory(folder);
        var name = MobileExchange.BaseName(purchase);
        var data = Path.Combine(folder, name + ".json");
        File.WriteAllBytes(data, MobileExchange.Serialize(purchase with { PhotoFileName = purchase.PhotoFileName is null ? null : name + ".jpg" }));
        if (withPhoto && purchase.PhotoFileName is not null) File.WriteAllBytes(Path.Combine(folder, name + ".jpg"), [1, 2, 3]);
        return data;
    }

    [Fact]
    public void A_purchase_round_trips_through_the_exchange_file_with_a_stable_name()
    {
        var purchase = Purchase();
        Assert.Equal("achat-2026-10-25-11111111", MobileExchange.BaseName(purchase));
        Assert.Equal(purchase, MobileExchange.Parse(MobileExchange.Serialize(purchase)));
    }

    [Fact]
    public void Invalid_or_too_new_files_are_refused()
    {
        Assert.Throws<JsonException>(() => MobileExchange.Serialize(Purchase(amount: 0)));
        Assert.Throws<JsonException>(() => MobileExchange.Serialize(Purchase(photo: @"..\autre.jpg")));
        Assert.Throws<JsonException>(() => MobileExchange.Parse("{ pas du json"u8));
        Assert.Throws<JsonException>(() => MobileExchange.Parse(ReadOnlySpan<byte>.Empty));
        var future = JsonSerializer.SerializeToUtf8Bytes(Purchase() with { Version = 99 });
        Assert.Throws<JsonException>(() => MobileExchange.Parse(future));
    }

    [Fact]
    public void The_computer_lists_waiting_purchases_with_their_photo_and_reports_incomplete_ones()
    {
        Drop(Purchase());
        var incomplete = Purchase() with { Id = Guid.Parse("99999999-0000-0000-0000-000000000000") };
        Drop(incomplete, withPhoto: false);
        File.WriteAllText(Path.Combine(MobileInbox.FolderFor(_root), "achat-corrompu.json"), "{ cassé");
        var scan = MobileInbox.Scan(_root);
        var item = Assert.Single(scan.Items);
        Assert.Equal("Leroy Merlin", item.Purchase.Supplier); Assert.True(File.Exists(item.PhotoPath));
        Assert.Equal(2, scan.Errors.Count);
        Assert.Contains(scan.Errors, e => e.Contains("pas encore arrivée"));
    }

    [Fact]
    public void A_missing_exchange_folder_is_simply_empty_and_processed_items_leave_the_inbox_without_overwriting()
    {
        Assert.Empty(MobileInbox.Scan(_root).Items);
        Drop(Purchase());
        var item = Assert.Single(MobileInbox.Scan(_root).Items);
        MobileInbox.MarkProcessed(_root, item);
        Assert.Empty(MobileInbox.Scan(_root).Items);
        var processed = Path.Combine(MobileInbox.FolderFor(_root), MobileExchange.ProcessedFolderName);
        Assert.Equal(2, Directory.GetFiles(processed).Length);
        Drop(Purchase());
        MobileInbox.MarkProcessed(_root, Assert.Single(MobileInbox.Scan(_root).Items));
        Assert.Equal(4, Directory.GetFiles(processed).Length);
    }

    [Fact]
    public void The_recorded_line_keeps_the_phone_id_and_names_a_missing_reference_after_the_ticket_date()
    {
        var entry = MobileExchange.ToEntry(Purchase(), "pieces/x.jpg");
        Assert.Equal(Purchase().Id, entry.Id); Assert.Equal("Ticket du 25/10/2026", entry.Reference);
        Assert.Equal("pieces/x.jpg", entry.DocumentPath); Assert.Equal("ABC-2026-001", entry.ProjectRef); Assert.Equal(18.9m, entry.Amount);
        Assert.Equal("FAC-9", MobileExchange.ToEntry(Purchase(reference: " FAC-9 "), null).Reference);
    }
}
