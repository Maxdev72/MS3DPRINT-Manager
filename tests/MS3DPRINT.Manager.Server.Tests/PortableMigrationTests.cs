using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MS3DPRINT.Manager.Core.Migration;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class PortableMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Orphaned_trash_payloads_are_refused(bool extraPayload)
    {
        using var host = new ServerFixture();
        MigrationTests.Seed(host.Workspace);
        var file = Path.Combine(host.Workspace, "01_CLIENTS", "CLIENT_TEST", "document.txt");
        File.WriteAllText(file, "keep");
        var entry = new ManagedFileService(host.Workspace).Trash(file);
        var trash = Path.Combine(host.Workspace, ".ms3dprint-manager", "trash", entry.Id.ToString("N"));
        if (extraPayload) File.WriteAllText(Path.Combine(trash, "999"), "orphan");
        else File.Delete(Path.Combine(trash, "manifest.json"));
        var archive = Path.Combine(host.Root, "source.zip");
        var hash = new WorkspaceBackupService().Create(host.Workspace, archive, true).ArchiveSha256;
        var result = new PortableWorkspaceMigrationService().Prepare(archive, Path.Combine(host.Root, "prepared"), hash);
        Assert.False(result.Succeeded);
        Assert.Equal(hash, Hash(archive));
    }

    [Theory]
    [InlineData("purchases", "DocumentPath", false)]
    [InlineData("revenues", "InvoicePdfPath", false)]
    [InlineData("purchases", "DocumentPath", true)]
    public void Journal_paths_are_converted_without_changing_money_identity_history_or_free_text(string kind, string field, bool privateReceipt)
    {
        using var host = new ServerFixture();
        MigrationTests.Seed(host.Workspace);
        var directory = Path.Combine(host.Workspace, ".ms3dprint-manager", kind);
        Directory.CreateDirectory(directory);
        var receipt = privateReceipt ? ".ms3dprint-manager/purchases/pieces/piece.pdf" : "08_COMPTABILITE/piece.pdf";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(host.Workspace, receipt))!);
        File.WriteAllText(Path.Combine(host.Workspace, receipt), "document");
        var id = Guid.NewGuid();
        var entry = new JsonObject { ["Id"] = id.ToString(), ["RecordedAt"] = "2026-11-03T06:00:00+00:00",
            ["Label"] = @"keep\notes", ["Amount"] = 123.45m, [field] = receipt.Replace('/', '\\'),
            ["PurchasedOn"] = "2026-11-03", ["ReceivedOn"] = "2026-11-03", ["Nature"] = "Goods",
            ["Supplier"] = "Test", ["PaymentMethod"] = "Carte", ["Reference"] = "R", ["InvoiceNumber"] = "F" };
        var correction = new JsonObject { ["Id"] = Guid.NewGuid().ToString(), ["EntryId"] = id.ToString(),
            ["Reason"] = @"keep\reason", ["CorrectedAt"] = "2026-11-03T07:00:00+00:00", ["Entry"] = entry.DeepClone() };
        var records = new[] {new JsonObject { ["Type"] = "entry", ["Entry"] = entry },
            new JsonObject { ["Type"] = "correction", ["Correction"] = correction }};
        var source = Path.Combine(directory, "2026.jsonl");
        File.WriteAllLines(source, records.Select(record => record.ToJsonString()));
        var before = File.ReadAllBytes(source);
        var archive = Path.Combine(host.Root, "source.zip");
        var hash = new WorkspaceBackupService().Create(host.Workspace, archive, true).ArchiveSha256;
        var prepared = Path.Combine(host.Root, "prepared");
        var result = new PortableWorkspaceMigrationService().Prepare(archive, prepared, hash);
        Assert.True(result.Succeeded, JsonSerializer.Serialize(result.Issues));
        var converted = File.ReadAllLines(Path.Combine(prepared, "workspace", ".ms3dprint-manager", kind, "2026.jsonl"))
            .Select(line => JsonNode.Parse(line)!).ToArray();
        foreach (var record in converted)
        {
            var convertedEntry = record["Entry"] ?? record["Correction"]!["Entry"]!;
            Assert.Equal(receipt, convertedEntry[field]!.GetValue<string>());
            convertedEntry[field] = receipt.Replace('/', '\\');
        }
        Assert.True(JsonNode.DeepEquals(records[0], converted[0]));
        Assert.True(JsonNode.DeepEquals(records[1], converted[1]));
        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Equal(hash, Hash(archive));
    }

    [Fact]
    public void Preparation_changes_only_known_path_fields_and_preserves_the_source_archive_and_documents()
    {
        using var host = new ServerFixture();
        var (client, project) = MigrationTests.Seed(host.Workspace);
        var paths = new WorkspaceMetadataPaths(host.Workspace);
        foreach (var file in Directory.GetFiles(paths.ClientsDirectory).Concat(Directory.GetFiles(paths.ProjectsDirectory)))
        {
            var json = JsonNode.Parse(File.ReadAllText(file))!;
            json["RelativePath"] = json["RelativePath"]!.GetValue<string>().Replace('/', '\\');
            json["Notes"] = @"keep\notes";
            json["FutureVersion"] = 17;
            File.WriteAllText(file, json.ToJsonString());
            File.Copy(file, Path.Combine(paths.HistoryDirectory, Path.GetFileNameWithoutExtension(file) + "-123.json"));
        }
        var collections = Path.Combine(paths.MetadataDirectory, "collections");
        Directory.CreateDirectory(collections);
        Directory.CreateDirectory(Path.Combine(host.Workspace, "02_MODELES_3D", "MODEL"));
        var collectionId = Guid.NewGuid();
        File.WriteAllText(Path.Combine(collections, collectionId + ".json"), JsonSerializer.Serialize(new
        {
            Id = collectionId, Category = "02_MODELES_3D", RelativePath = @"02_MODELES_3D\MODEL", Name = "Model",
            CreatedAt = client.CreatedAt, UpdatedAt = client.UpdatedAt
        }));
        var original = Path.Combine(host.Workspace, "01_CLIENTS", "CLIENT_TEST", "old.txt");
        File.WriteAllText(original, "restore me");
        var trash = new ManagedFileService(host.Workspace).Trash(original);
        var trashFile = Directory.GetFiles(Path.Combine(paths.MetadataDirectory, "trash"), "manifest.json", SearchOption.AllDirectories).Single();
        var trashJson = JsonNode.Parse(File.ReadAllText(trashFile))!;
        trashJson["Items"]![0]!["Original"] = @"01_CLIENTS\CLIENT_TEST\old.txt";
        File.WriteAllText(trashFile, trashJson.ToJsonString());
        var archive = Path.Combine(host.Root, "source.zip");
        var hash = new WorkspaceBackupService().Create(host.Workspace, archive, true).ArchiveSha256;
        var sourceHashes = Hashes(host.Workspace);
        var prepared = Path.Combine(host.Root, "prepared");
        var result = new PortableWorkspaceMigrationService().Prepare(archive, prepared, hash);
        Assert.True(result.Succeeded, JsonSerializer.Serialize(result.Issues));
        Assert.Equal(hash, Hash(archive));
        Assert.Equal(hash, Hash(Path.Combine(prepared, "source.zip")));
        Assert.Equal(sourceHashes, Hashes(host.Workspace));
        var restored = Path.Combine(prepared, "workspace");
        var jsonResult = JsonNode.Parse(File.ReadAllText(Path.Combine(restored, ".ms3dprint-manager", "projects", project.Id + ".json")))!;
        Assert.Equal("01_CLIENTS/CLIENT_TEST/CT-2026-001_TEST", jsonResult["RelativePath"]!.GetValue<string>());
        Assert.Equal(project.Id, jsonResult["Id"]!.GetValue<Guid>());
        Assert.Equal(project.UpdatedAt, jsonResult["UpdatedAt"]!.GetValue<DateTimeOffset>());
        Assert.Equal(17, jsonResult["FutureVersion"]!.GetValue<int>());
        Assert.Equal(@"keep\notes", jsonResult["Notes"]!.GetValue<string>());
        Assert.Equal(Hash(Path.Combine(host.Workspace, "01_CLIENTS", "CLIENT_TEST", project.FolderName, "piece.stl")), Hash(Path.Combine(restored, "01_CLIENTS", "CLIENT_TEST", project.FolderName, "piece.stl")));
        Assert.Equal(6, result.Transformations.Count);
        Assert.All(result.Transformations, change => Assert.NotEqual(change.BeforeSha256, change.AfterSha256));
        Assert.True(File.Exists(Path.Combine(prepared, "transformations.json")));
        Assert.True(File.Exists(Path.Combine(prepared, "portable-report.json")));
        Assert.True(File.Exists(Path.Combine(prepared, "portable.zip")));
        var verified = JsonSerializer.Deserialize<MigrationReport>(File.ReadAllText(Path.Combine(prepared, "verification", "report.json")))!;
        Assert.True(verified.Succeeded);
        new ManagedFileService(restored).Restore(trash.Id);
        Assert.Equal("restore me", File.ReadAllText(Path.Combine(restored, "01_CLIENTS", "CLIENT_TEST", "old.txt")));
    }

    [Theory]
    [InlineData("absolute")]
    [InlineData("traversal")]
    [InlineData("unknown-schema")]
    [InlineData("unknown-field")]
    [InlineData("bad-schema")]
    [InlineData("bad-trash")]
    [InlineData("missing-trash-payload")]
    public void Unsafe_or_unrecognized_metadata_cannot_produce_a_portable_archive(string problem)
    {
        using var host = new ServerFixture();
        var (client, _) = MigrationTests.Seed(host.Workspace);
        var metadata = Path.Combine(host.Workspace, ".ms3dprint-manager");
        var file = Path.Combine(metadata, "clients", client.Id + ".json");
        var json = JsonNode.Parse(File.ReadAllText(file))!;
        if (problem == "absolute") json["RelativePath"] = @"C:\old\client";
        if (problem == "traversal") json["RelativePath"] = @"01_CLIENTS\..\secret";
        if (problem == "bad-schema") { json["Kind"] = 999; json["RelativePath"] = @"01_CLIENTS\CLIENT_TEST"; }
        if (problem == "unknown-field") json["FuturePath"] = @"01_CLIENTS\CLIENT_TEST";
        File.WriteAllText(file, json.ToJsonString());
        if (problem == "unknown-schema") File.WriteAllText(Path.Combine(metadata, "future.json"), "{\"RelativePath\":\"01_CLIENTS\\\\CLIENT_TEST\"}");
        if (problem is "bad-trash" or "missing-trash-payload")
        {
            var trash = Path.Combine(metadata, "trash", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(trash);
            File.WriteAllText(Path.Combine(trash, "manifest.json"), problem == "bad-trash"
                ? "{\"Items\":[{\"Original\":\"01_CLIENTS\\\\CLIENT_TEST\",\"Payload\":\"0\"}]}"
                : JsonSerializer.Serialize(new { Entry = new TrashEntry(Guid.ParseExact(Path.GetFileName(trash), "N"), "Old", DateTimeOffset.UtcNow), State = "Trashed",
                    Items = new[] { new { Original = @"01_CLIENTS\CLIENT_TEST\old.txt", Payload = "0", IsDirectory = false } } }));
        }
        var archive = Path.Combine(host.Root, "source.zip");
        var hash = new WorkspaceBackupService().Create(host.Workspace, archive, true).ArchiveSha256;
        var destination = Path.Combine(host.Root, "prepared");
        var result = new PortableWorkspaceMigrationService().Prepare(archive, destination, hash);
        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Issues);
        if (problem is "bad-schema" or "bad-trash" or "missing-trash-payload")
            Assert.Contains(result.Issues, issue => issue.Path.StartsWith(".ms3dprint-manager/", StringComparison.Ordinal));
        Assert.Equal(hash, Hash(archive));
        Assert.False(File.Exists(Path.Combine(destination, "portable.zip")));
        Assert.True(File.Exists(Path.Combine(destination, "portable-report.json")));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("truncated")]
    [InlineData("metadata-only")]
    public void Invalid_input_stays_isolated(string problem)
    {
        using var host = new ServerFixture();
        MigrationTests.Seed(host.Workspace);
        var archive = Path.Combine(host.Root, "source.zip");
        var hash = new WorkspaceBackupService().Create(host.Workspace, archive, problem != "metadata-only").ArchiveSha256;
        if (problem == "truncated") { File.WriteAllBytes(archive, [1, 2, 3]); hash = Hash(archive); }
        var before = Hashes(host.Workspace);
        var destination = Path.Combine(host.Root, "prepared");
        var result = new PortableWorkspaceMigrationService().Prepare(archive, destination, problem == "hash" ? new string('0', 64) : hash);
        Assert.False(result.Succeeded);
        Assert.Equal(hash, Hash(archive));
        Assert.Equal(before, Hashes(host.Workspace));
        Assert.False(Directory.Exists(Path.Combine(destination, "workspace")));
        Assert.True(File.Exists(Path.Combine(destination, "portable-report.json")));
    }

    [Fact]
    public void Existing_destination_is_never_overwritten()
    {
        using var host = new ServerFixture();
        var directory = Path.Combine(host.Root, "existing");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "keep"), "keep");
        Assert.Throws<IOException>(() => new PortableWorkspaceMigrationService().Prepare("missing.zip", directory, new string('0', 64)));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(directory, "keep")));
    }

    [Fact]
    public void Trashed_profile_payload_is_portable_when_restored_on_another_operating_system()
    {
        using var host = new ServerFixture();
        var (client, project) = MigrationTests.Seed(host.Workspace);
        var file = Path.Combine(host.Workspace, ".ms3dprint-manager", "projects", project.Id + ".json");
        var json = JsonNode.Parse(File.ReadAllText(file))!;
        json["RelativePath"] = @"01_CLIENTS\CLIENT_TEST\CT-2026-001_TEST";
        File.WriteAllText(file, json.ToJsonString());
        var folder = Path.Combine(host.Workspace, "01_CLIENTS", "CLIENT_TEST", project.FolderName);
        var trash = new ManagedFileService(host.Workspace).Trash(folder, [file]);
        var archive = Path.Combine(host.Root, "source.zip");
        var hash = new WorkspaceBackupService().Create(host.Workspace, archive, true).ArchiveSha256;
        var preparation = Path.Combine(host.Root, "prepared");
        var result = new PortableWorkspaceMigrationService().Prepare(archive, preparation, hash);
        Assert.True(result.Succeeded, JsonSerializer.Serialize(result.Issues));
        var restored = Path.Combine(preparation, "workspace");
        new ManagedFileService(restored).Restore(trash.Id);
        var restoredProfile = JsonNode.Parse(File.ReadAllText(Path.Combine(restored, ".ms3dprint-manager", "projects", project.Id + ".json")))!;
        Assert.Equal("01_CLIENTS/CLIENT_TEST/CT-2026-001_TEST", restoredProfile["RelativePath"]!.GetValue<string>());
        Assert.Equal(project.Id, restoredProfile["Id"]!.GetValue<Guid>());
        Assert.Equal(project.UpdatedAt, restoredProfile["UpdatedAt"]!.GetValue<DateTimeOffset>());
    }

    [Fact]
    public void Traversal_in_a_forged_archive_is_rejected_before_extraction()
    {
        using var host = new ServerFixture();
        var archive = Path.Combine(host.Root, "hostile.zip");
        Directory.CreateDirectory(host.Root);
        using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var entry = new StreamWriter(zip.CreateEntry("workspace/../escaped.txt").Open())) entry.Write("escape");
            using var manifest = new StreamWriter(zip.CreateEntry("ms3dprint-backup.json").Open());
            manifest.Write(JsonSerializer.Serialize(new { Version = 1, CreatedAt = DateTimeOffset.UtcNow, IncludesDocuments = true,
                Directories = Array.Empty<string>(), Files = new[] { new { Path = "../escaped.txt", Bytes = 6, Sha256 = new string('0', 64) } } }));
        }
        var result = new PortableWorkspaceMigrationService().Prepare(archive, Path.Combine(host.Root, "prepared"), Hash(archive));
        Assert.False(result.Succeeded);
        Assert.False(File.Exists(Path.Combine(host.Root, "escaped.txt")));
        Assert.False(Directory.Exists(Path.Combine(host.Root, "prepared", "workspace")));
    }

    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    private static string[] Hashes(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(root, file) + ":" + Hash(file)).Order(StringComparer.Ordinal).ToArray();
}
