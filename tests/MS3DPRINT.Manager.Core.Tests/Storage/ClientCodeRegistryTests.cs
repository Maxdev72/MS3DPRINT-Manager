using System.Text.Json;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Tests.Storage;

public sealed class ClientCodeRegistryTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "MS3DPRINT-registry-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Add_PersistsAndCanBeReadByANewRegistry()
    {
        new ClientCodeRegistry(_dataDirectory).Add("DUPONT", "DUPONT");

        Assert.Equal("DUPONT", new ClientCodeRegistry(_dataDirectory).GetCode("DUPONT"));
        Assert.True(File.Exists(Path.Combine(_dataDirectory, "client-codes.json")));
    }

    [Fact]
    public void Add_RejectsExistingNormalizedClientWithoutChangingCode()
    {
        var registry = new ClientCodeRegistry(_dataDirectory);
        registry.Add("Société Dupont", "SD");
        var activePath = Path.Combine(_dataDirectory, "client-codes.json");
        var priorJson = File.ReadAllText(activePath);

        Assert.Throws<FolderConflictException>(() => registry.Add("SOCIETE_DUPONT", "OTHER"));

        Assert.Equal("SD", registry.GetCode("société dupont"));
        Assert.Equal(priorJson, File.ReadAllText(activePath));
    }

    [Fact]
    public void Add_PreservesPriorActiveJsonBeforeActivatingNewJson()
    {
        var registry = new ClientCodeRegistry(_dataDirectory);
        registry.Add("DUPONT", "DUPONT");
        var activePath = Path.Combine(_dataDirectory, "client-codes.json");
        var priorJson = File.ReadAllText(activePath);

        registry.Add("MARTIN", "MARTIN");

        Assert.Equal(priorJson, File.ReadAllText(Path.Combine(_dataDirectory, "client-codes.previous.json")));
        var codes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(activePath));
        Assert.NotNull(codes);
        Assert.Equal("DUPONT", codes["DUPONT"]);
        Assert.Equal("MARTIN", codes["MARTIN"]);
        Assert.Empty(Directory.GetFiles(_dataDirectory, "client-codes.*.tmp"));
    }

    [Fact]
    public void Add_WithExistingBackupPreservesMostRecentActiveJson()
    {
        var registry = new ClientCodeRegistry(_dataDirectory);
        registry.Add("DUPONT", "DUPONT");
        var firstActiveJson = File.ReadAllText(Path.Combine(_dataDirectory, "client-codes.json"));
        registry.Add("MARTIN", "MARTIN");
        var activePath = Path.Combine(_dataDirectory, "client-codes.json");
        var priorJson = File.ReadAllText(activePath);

        registry.Add("MOREAU", "MOREAU");

        Assert.Equal(priorJson, File.ReadAllText(Path.Combine(_dataDirectory, "client-codes.previous.json")));
        var archivedBackups = Directory.GetFiles(_dataDirectory, "client-codes.previous.*.json");
        Assert.Single(archivedBackups);
        Assert.Equal(firstActiveJson, File.ReadAllText(archivedBackups[0]));
        Assert.Equal("MOREAU", new ClientCodeRegistry(_dataDirectory).GetCode("MOREAU"));
    }

    [Fact]
    public void Add_DoesNotReplaceMalformedExistingJson()
    {
        Directory.CreateDirectory(_dataDirectory);
        var activePath = Path.Combine(_dataDirectory, "client-codes.json");
        File.WriteAllText(activePath, "{broken");

        Assert.Throws<JsonException>(() => new ClientCodeRegistry(_dataDirectory).Add("DUPONT", "DUPONT"));

        Assert.Equal("{broken", File.ReadAllText(activePath));
        Assert.False(File.Exists(Path.Combine(_dataDirectory, "client-codes.previous.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }
}
