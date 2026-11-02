namespace MS3DPRINT.Manager.Server;

public partial class Program
{
    public static int Main(string[] args)
    {
        var createOwner = args.Contains("--create-owner", StringComparer.Ordinal);
        using var app = ServerApplication.Build(args.Where(a => a != "--create-owner").ToArray());
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
}
