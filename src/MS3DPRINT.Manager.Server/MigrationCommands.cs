using MS3DPRINT.Manager.Core.Migration;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Server;

public static class MigrationCommands
{
    public static int Execute(string[] args, ServerStorage storage, TextWriter output)
    {
        var export = args.Length == 2 && args[0] == "--export-migration" && !string.IsNullOrWhiteSpace(args[1]);
        var portable = args.Length == 4 && args[0] == "--prepare-portable-migration";
        var stage = args.Length == 4 && (args[0] == "--stage-migration" || portable) && !string.IsNullOrWhiteSpace(args[1])
            && args[2] == "--sha256" && args[3].Length == 64 && args[3].All(Uri.IsHexDigit);
        if (!export && !stage)
        {
            output.WriteLine("Utilisation : --export-migration <nouveau-dossier> OU --stage-migration <archive> --sha256 <empreinte> OU --prepare-portable-migration <archive> --sha256 <empreinte>.");
            return 1;
        }
        try
        {
            var service = new WorkspaceMigrationService();
            string directory;
            MigrationReport report;
            if (export)
            {
                directory = Path.GetFullPath(args[1]);
                report = service.Export(storage.WorkspaceRoot, directory);
            }
            else
            {
                var parent = Path.Combine(storage.StateRoot, "migrations");
                WorkspacePathSafety.EnsureNoLinks(parent);
                Directory.CreateDirectory(parent);
                directory = Path.Combine(parent, Guid.NewGuid().ToString("N"));
                if (portable)
                {
                    var prepared = new PortableWorkspaceMigrationService().Prepare(args[1], directory, args[3]);
                    output.WriteLine("Bilan : " + Path.Combine(directory, "portable-report.json"));
                    output.WriteLine("Transformations : " + Path.Combine(directory, "transformations.json"));
                    if (prepared.PortableArchiveSha256 is not null)
                    {
                        output.WriteLine("Archive portable : " + Path.Combine(directory, "portable.zip"));
                        output.WriteLine("SHA256 : " + prepared.PortableArchiveSha256);
                    }
                    output.WriteLine(prepared.Succeeded ? "Contrôles réussis. Aucune activation effectuée." : "Contrôles refusés. Aucune activation effectuée.");
                    return prepared.Succeeded ? 0 : 2;
                }
                report = service.Stage(args[1], directory, args[3]);
            }
            output.WriteLine("Bilan : " + Path.Combine(directory, "report.json"));
            if (report.ArchiveSha256 is not null) output.WriteLine("SHA256 : " + report.ArchiveSha256);
            output.WriteLine(report.Succeeded ? "Contrôles réussis. Aucune activation effectuée." : "Contrôles refusés. Aucune activation effectuée.");
            return report.Succeeded ? 0 : 2;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            output.WriteLine("Préparation refusée (" + e.GetType().Name + "). Utilisez une source accessible et un nouveau dossier. Aucune activation effectuée.");
            return 2;
        }
    }
}
