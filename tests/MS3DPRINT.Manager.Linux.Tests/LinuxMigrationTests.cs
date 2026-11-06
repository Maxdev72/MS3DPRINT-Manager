using MS3DPRINT.Manager.Core.Migration;

namespace MS3DPRINT.Manager.Linux.Tests;

public sealed class LinuxMigrationTests
{
    [Fact]
    public void Journal_identity_distinguishes_case_sensitive_workshops_and_backup_is_private()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Directory.CreateTempSubdirectory("linux-migration-identity-").FullName;
        GuidedWorkspaceMigration? wrong = null;
        try
        {
            var first = Path.Combine(directory, "PC"); var second = Path.Combine(directory, "pc");
            foreach (var root in new[] { first, second })
            {
                Directory.CreateDirectory(Path.Combine(root, "02_MODELES_3D"));
                File.WriteAllText(Path.Combine(root, "02_MODELES_3D", "model.txt"), "Identical content does not mean the same workshop");
            }
            var server = new Uri("https://example.test/"); string journal;
            using (var migration = GuidedWorkspaceMigration.Create(server, "owner", first, Path.Combine(directory, "private")))
            {
                journal = migration.JournalPath;
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(migration.Journal.Directory));
            }
            Assert.Throws<GuidedWorkspaceMigrationIdentityException>(() => wrong = GuidedWorkspaceMigration.Load(journal, server, "owner", second));
        }
        finally { wrong?.Dispose(); Directory.Delete(directory, true); }
    }
}
