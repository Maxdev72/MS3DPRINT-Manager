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
}
