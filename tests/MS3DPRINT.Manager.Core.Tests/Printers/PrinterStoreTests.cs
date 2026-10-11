using MS3DPRINT.Manager.Core.Printers;

namespace MS3DPRINT.Manager.Core.Tests.Printers;

public sealed class PrinterStoreTests
{
    [Fact]
    public void Create_then_load_returns_the_registered_printer()
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3d-printer-" + Guid.NewGuid().ToString("N"));
        var store = new PrinterStore(root);
        var profile = new PrinterProfile(Guid.NewGuid(), "Bambu Lab", "P1S", "P1S Atelier", 2.50m, "256 × 256 × 256 mm", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        store.Create(profile);

        var actual = Assert.Single(store.LoadAll());
        Assert.Equal(profile, actual);
    }
}
