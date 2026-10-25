using System.Text.Json;
using MS3DPRINT.Manager.Core.Mobile;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Mobile;

public sealed class MobileRevenueAndSnapshotTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-mobile2-" + Guid.NewGuid().ToString("N"));
    public MobileRevenueAndSnapshotTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static MobileRevenue Revenue(RevenueNature nature = RevenueNature.Services, decimal amount = 120m, decimal goods = 0m, string? invoice = "F-12", string? photo = null) =>
        new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), new DateOnly(2026, 10, 24), "Impression de supports", amount, nature, goods, "Virement", "Atelier Martin", invoice, "ABC-2026-001", photo,
            new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_revenue_round_trips_with_its_nature_as_text_and_a_stable_name()
    {
        var revenue = Revenue(RevenueNature.Mixed, 100m, 30m);
        var bytes = MobileRevenueExchange.Serialize(revenue);
        Assert.Contains("\"Mixed\"", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.Equal(revenue, MobileRevenueExchange.Parse(bytes));
        Assert.Equal("recette-2026-10-24-aaaaaaaa", MobileRevenueExchange.BaseName(revenue));
    }

    [Fact]
    public void Invalid_revenues_are_refused()
    {
        Assert.Throws<JsonException>(() => MobileRevenueExchange.Serialize(Revenue(amount: 0)));
        Assert.Throws<JsonException>(() => MobileRevenueExchange.Serialize(Revenue() with { Label = " " }));
        Assert.Throws<JsonException>(() => MobileRevenueExchange.Serialize(Revenue(RevenueNature.Mixed, 100m, 0m)));
        Assert.Throws<JsonException>(() => MobileRevenueExchange.Serialize(Revenue(RevenueNature.Mixed, 100m, 100m)));
        Assert.Throws<JsonException>(() => MobileRevenueExchange.Serialize(Revenue(photo: @"..\x.jpg")));
        Assert.Throws<JsonException>(() => MobileRevenueExchange.Parse(JsonSerializer.SerializeToUtf8Bytes(Revenue() with { Version = 9 })));
    }

    [Fact]
    public void The_recorded_line_follows_the_nature_and_names_a_missing_receipt()
    {
        var services = MobileRevenueExchange.ToEntry(Revenue(), "pieces/x.jpg");
        Assert.Equal(0m, services.GoodsAmount); Assert.Equal(120m, services.ServicesAmount); Assert.Equal("F-12", services.InvoiceNumber); Assert.Equal("pieces/x.jpg", services.InvoicePdfPath);
        var goods = MobileRevenueExchange.ToEntry(Revenue(RevenueNature.Goods), null);
        Assert.Equal(120m, goods.GoodsAmount); Assert.Equal(0m, goods.ServicesAmount);
        var mixed = MobileRevenueExchange.ToEntry(Revenue(RevenueNature.Mixed, 100m, 30m, invoice: null), null);
        Assert.Equal(30m, mixed.GoodsAmount); Assert.Equal(70m, mixed.ServicesAmount); Assert.Equal("Sans pièce du 24/10/2026", mixed.InvoiceNumber);
        Assert.Equal(Revenue().Id, mixed.Id);
    }

    [Fact]
    public void The_inbox_lists_purchases_and_revenues_apart_and_files_both_away()
    {
        var folder = MobileInbox.FolderFor(_root); Directory.CreateDirectory(folder);
        var revenue = Revenue(photo: "recette-2026-10-24-aaaaaaaa.jpg");
        File.WriteAllBytes(Path.Combine(folder, "recette-2026-10-24-aaaaaaaa.json"), MobileRevenueExchange.Serialize(revenue));
        File.WriteAllBytes(Path.Combine(folder, "recette-2026-10-24-aaaaaaaa.jpg"), [1, 2, 3]);
        var purchase = new MobilePurchase(Guid.NewGuid(), new DateOnly(2026, 10, 24), "Bambu Lab", "Bobines", 40m, "Carte bancaire", null, null, null, DateTimeOffset.UtcNow);
        File.WriteAllBytes(Path.Combine(folder, MobileExchange.BaseName(purchase) + ".json"), MobileExchange.Serialize(purchase));
        File.WriteAllText(Path.Combine(folder, "recette-casse.json"), "{ cassé");
        File.WriteAllText(Path.Combine(folder, PcSnapshot.FileName), "{}");
        var scan = MobileInbox.Scan(_root);
        Assert.Single(scan.Items); var item = Assert.Single(scan.Revenues); Assert.Single(scan.Errors);
        Assert.NotNull(item.PhotoPath);
        MobileInbox.MarkProcessed(_root, item);
        Assert.Empty(MobileInbox.Scan(_root).Revenues);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(folder, MobileExchange.ProcessedFolderName)).Length);
        Assert.True(File.Exists(Path.Combine(folder, PcSnapshot.FileName)));
    }

    private static PcSnapshot Snapshot() => new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero), "MS3DPRINT",
        [new SnapshotClient("Atelier Martin", "MAR")],
        [new SnapshotProject("MAR-2026-001", "Support caméra", "Atelier Martin", "InProgress", new DateOnly(2026, 11, 3), "High", false, 150m, 60m, 12.5m)],
        new SnapshotActivity(2026, 1234.5m, 200m, 1034.5m, [0, 0, 0, 0, 0, 0, 0, 0, 0, 1234.5m, 0, 0], 80m,
            [new SnapshotThreshold("Plafond micro", 1234.5m, 203100m, "Ok")], 2),
        ["Bambu Lab", "Leroy Merlin"]);

    [Fact]
    public void The_pc_snapshot_round_trips_through_the_exchange_folder_and_is_replaced_atomically()
    {
        Assert.Null(PcSnapshotFile.Read(_root));
        PcSnapshotFile.Write(_root, Snapshot());
        var read = PcSnapshotFile.Read(_root)!;
        Assert.Equal("MS3DPRINT", read.WorkshopName); Assert.Equal(new DateOnly(2026, 11, 3), read.Projects[0].DueDate); Assert.Equal(12, read.Activity.Months.Count);
        Assert.Equal("Ok", read.Activity.Thresholds[0].Level); Assert.Equal(2, read.Activity.PendingFromPhone);
        PcSnapshotFile.Write(_root, Snapshot() with { WorkshopName = "Autre" });
        Assert.Equal("Autre", PcSnapshotFile.Read(_root)!.WorkshopName);
        Assert.Empty(Directory.GetFiles(MobileInbox.FolderFor(_root), "*.tmp"));
    }

    [Fact]
    public void A_snapshot_from_a_newer_version_or_garbage_is_refused()
    {
        Assert.Throws<JsonException>(() => PcSnapshotFile.Parse(JsonSerializer.SerializeToUtf8Bytes(Snapshot() with { Version = 99 })));
        Assert.Throws<JsonException>(() => PcSnapshotFile.Parse("{ nope"u8));
        Assert.Throws<JsonException>(() => PcSnapshotFile.Parse(ReadOnlySpan<byte>.Empty));
    }
}
