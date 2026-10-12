using MS3DPRINT.Manager.Core.Estimates;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed class EstimateStoreTests
{
    [Fact]
    public void Create_then_load_returns_the_saved_estimate()
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3d-estimate-" + Guid.NewGuid().ToString("N"));
        var estimate = new EstimateProfile(Guid.NewGuid(), "Support bobine", "Sunlu Basic", "P1S Atelier", new EstimateInputs(MaterialCost: 4m, MachineCost: 2m, MarginPercent: 30m), new EstimateResult(6m, 6m, 7.8m, 7.8m), DateTimeOffset.UtcNow);

        new EstimateStore(root).Create(estimate);

        Assert.Equal(estimate, Assert.Single(new EstimateStore(root).LoadAll()));
    }

    [Fact]
    public void Update_replaces_the_saved_estimate()
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3d-estimate-" + Guid.NewGuid().ToString("N"));
        var now = DateTimeOffset.UtcNow;
        var estimate = new EstimateProfile(Guid.NewGuid(), "Support", "Sunlu Basic", "P1S", new EstimateInputs(MaterialCost: 4m), new EstimateResult(4m, 4m, 4m, 4m), now);
        var store = new EstimateStore(root);
        store.Create(estimate);

        store.Update(estimate with { Name = "Support révisé" });

        Assert.Equal("Support révisé", Assert.Single(store.LoadAll()).Name);
    }
}
