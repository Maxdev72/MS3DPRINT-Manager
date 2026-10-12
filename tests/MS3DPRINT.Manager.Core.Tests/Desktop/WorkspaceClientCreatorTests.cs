using MS3DPRINT.Manager.Desktop.Dashboard;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WorkspaceClientCreatorTests
{
    [Fact]
    public void Create_professional_creates_the_client_folder_and_profile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-client-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS"));
        try
        {
            var profile = new WorkspaceClientCreator().CreateProfessional(root, "Atelier Nord", "NORD");
            Assert.True(Directory.Exists(Path.Combine(root, "01_CLIENTS", profile.FolderName)));
            Assert.True(File.Exists(Path.Combine(root, ".ms3dprint-manager", "clients", profile.Id + ".json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("Fell agency", "FELL_AGENCY")]
    [InlineData("Société Générale & Fils", "SOCIETE_GENERALE_FILS")]
    public void Create_professional_normalizes_the_folder_name(string name, string expectedFolder)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-client-name-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS"));
        try
        {
            var profile = new WorkspaceClientCreator().CreateProfessional(root, name, "fa");

            Assert.Equal(expectedFolder, profile.FolderName);
            Assert.Equal("FA", profile.ClientCode);
            Assert.Equal(name, profile.CompanyName);
            Assert.True(Directory.Exists(Path.Combine(root, "01_CLIENTS", expectedFolder)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Create_individual_names_the_folder_firstname_then_lastname_like_existing_clients()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-person-name-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS"));
        try
        {
            var profile = new WorkspaceClientCreator().CreateIndividual(root, "Jean-Noël", "Roisné", "jnr");

            Assert.Equal("JEAN_NOEL_ROISNE", profile.FolderName);
            Assert.Equal("JNR", profile.ClientCode);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Create_individual_creates_a_personal_client_profile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-person-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS"));
        try
        {
            var profile = new WorkspaceClientCreator().CreateIndividual(root, "Camille", "Durand", "DURAND");

            Assert.Equal(ClientKind.Individual, profile.Kind);
            Assert.Equal("Camille", profile.FirstName);
            Assert.True(Directory.Exists(Path.Combine(root, "01_CLIENTS", profile.FolderName)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Complete_existing_client_preserves_its_legacy_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-legacy-client-{Guid.NewGuid():N}");
        var folder = Path.Combine(root, "01_CLIENTS", "CLIENT HISTORIQUE");
        Directory.CreateDirectory(folder);
        try
        {
            var profile = new WorkspaceClientCreator().CompleteExisting(root, folder, ClientKind.Professional, "CLIENT HISTORIQUE", "HISTO", null, null);

            Assert.Equal("CLIENT HISTORIQUE", profile.FolderName);
            Assert.Equal("01_CLIENTS/CLIENT HISTORIQUE", profile.RelativePath?.Replace('\\', '/'));
            Assert.True(Directory.Exists(folder));
        }
        finally { Directory.Delete(root, true); }
    }
}
