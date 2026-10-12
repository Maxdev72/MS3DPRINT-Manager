using MS3DPRINT.Manager.Core.Filaments;

namespace MS3DPRINT.Manager.Core.Tests.Filaments;

public sealed class FilamentColorTests
{
    [Fact]
    public void Profile_keeps_its_optional_color()
    {
        var profile = new FilamentProfile(Guid.NewGuid(), "Sunlu", "PLA Basic", "PLA", 19.90m, false,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Color: "Noir mat");

        Assert.Equal("Noir mat", profile.Color);
    }
}
