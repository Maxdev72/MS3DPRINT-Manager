using MS3DPRINT.Manager.Core.Estimates;
using System.Text.Json;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed class EstimateStoreResilienceTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Name\":\"Incomplet\",\"Result\":null}")]
    [InlineData("{ invalide")]
    public void Unreadable_estimates_do_not_hide_or_block_trashing_a_healthy_quote(string corrupt)
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3d-quote-resilience-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new EstimateStore(root);
            var quote = new EstimateProfile(Guid.NewGuid(), "Devis valide", "PLA", "Machine", new(), new(1, 2, 3, 4), DateTimeOffset.UtcNow);
            store.Create(quote);
            var invalid = Path.Combine(root, ".ms3dprint-manager", "estimates", Guid.NewGuid() + ".json");
            File.WriteAllText(invalid, corrupt);
            Assert.Equal(quote, Assert.Single(store.LoadReadable()));
            store.Trash(quote.Id);
            Assert.True(File.Exists(invalid));
            Assert.Empty(store.LoadReadable());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
