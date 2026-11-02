using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using System.Threading.RateLimiting;
using MS3DPRINT.Manager.Server.Authentication;
using MS3DPRINT.Manager.Server.Workspace;

namespace MS3DPRINT.Manager.Server;

public static class ServerApplication
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var address in builder.Configuration.GetSection("Server:TrustedProxyAddresses").GetChildren())
                options.KnownProxies.Add(IPAddress.Parse(address.Value ?? ""));
            options.ForwardLimit = 1;
            options.ForwardedHeaders = options.KnownProxies.Count == 0 ? ForwardedHeaders.None : ForwardedHeaders.XForwardedProto;
        });
        builder.Services.AddSingleton<ServerStorage>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SessionStore>();
        builder.Services.AddSingleton<WorkspaceReader>();
        builder.Services.AddSingleton<CommandStore>();
        builder.Services.AddSingleton<WorkspaceCommands>();
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false)));
        builder.Services.AddDataProtection().SetApplicationName("MS3DFORGE.Server");
        builder.Services.AddOptions<KeyManagementOptions>().Configure<ServerStorage, ILoggerFactory>((options, storage, logger) =>
            options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(Path.Combine(storage.StateRoot, "keys")), logger));
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-ms3dforge-csrf";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("login", _ => RateLimitPartition.GetFixedWindowLimiter("owner-login", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));
        });
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 8192);
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        var app = builder.Build();
        _ = app.Services.GetRequiredService<ServerStorage>();
        _ = app.Services.GetRequiredService<SessionStore>();
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            if (!context.Request.IsHttps && context.Request.Path != "/health")
            {
                await Results.Problem(statusCode: 400, title: "Une connexion HTTPS est requise.").ExecuteAsync(context);
                return;
            }
            await next(context);
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapAuthentication();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapWorkspace();

        return app;
    }
}
