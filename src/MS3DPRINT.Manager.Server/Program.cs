namespace MS3DPRINT.Manager.Server;

public partial class Program
{
    public static int Main(string[] args)
    {
        var createOwner = args.Contains("--create-owner", StringComparer.Ordinal);
        var migration = args.Any(a => a is "--export-migration" or "--stage-migration" or "--prepare-portable-migration");
        if (createOwner && migration)
        {
            Console.Error.WriteLine("Exécutez la création du propriétaire et la préparation de migration séparément.");
            return 1;
        }
        var uncertain = args.FirstOrDefault() is "--list-uncertain-commands" or "--resolve-uncertain-command";
        using var app = ServerApplication.Build(migration || uncertain ? [] : args.Where(a => a != "--create-owner").ToArray());
        if (migration) return MigrationCommands.Execute(args, app.Services.GetRequiredService<ServerStorage>(), Console.Out);
        if (uncertain) return UncertainCommands(args, app.Services.GetRequiredService<Workspace.CommandStore>());
        if (createOwner)
        {
            try
            {
                var name = Environment.GetEnvironmentVariable("MS3DFORGE_OWNER_NAME");
                var password = Environment.GetEnvironmentVariable("MS3DFORGE_OWNER_PASSWORD");
                Environment.SetEnvironmentVariable("MS3DFORGE_OWNER_PASSWORD", null);
                if (name is null || password is null) throw new ArgumentException("Renseignez MS3DFORGE_OWNER_NAME et MS3DFORGE_OWNER_PASSWORD pour cette commande.");
                app.Services.GetRequiredService<Authentication.SessionStore>().CreateOwner(name, password);
                Console.WriteLine("Propriétaire créé. Retirez les variables de bootstrap de votre environnement.");
                return 0;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                Console.Error.WriteLine(e.Message);
                return 1;
            }
        }
        app.Run();
        return 0;
    }

    private static int UncertainCommands(string[] args, Workspace.CommandStore commands)
    {
        if (args is ["--list-uncertain-commands"])
        {
            foreach (var id in commands.UncertainCommands()) Console.WriteLine(id);
            return 0;
        }
        if (args is ["--resolve-uncertain-command", var value, "--confirm"] && Guid.TryParse(value, out var command) && command != Guid.Empty)
        {
            if (commands.ResolveUncertain(command)) { Console.WriteLine("Commande clôturée. Les écritures reprennent si aucune autre n’est incertaine."); return 0; }
            Console.Error.WriteLine("Aucune commande incertaine ne porte cet identifiant.");
            return 1;
        }
        Console.Error.WriteLine("Utilisation : --list-uncertain-commands OU --resolve-uncertain-command <identifiant> --confirm (après vérification des fiches).");
        return 1;
    }
}
