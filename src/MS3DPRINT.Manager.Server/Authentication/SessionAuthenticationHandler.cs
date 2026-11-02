using System.Text.Encodings.Web;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MS3DPRINT.Manager.Server.Authentication;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, SessionStore sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";
    public const string CookieName = "__Host-ms3dforge";
    public const string TokenItem = "ms3dforge-session-token";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        var cookie = Request.Cookies[CookieName];
        var bearer = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        if (authorization.Length > 0 && (!bearer || cookie is not null))
            return Task.FromResult(AuthenticateResult.Fail("Authentification ambiguë ou invalide."));
        var token = bearer ? authorization[7..] : cookie;
        if (token is null) return Task.FromResult(AuthenticateResult.NoResult());
        if (!sessions.IsActive(token)) return Task.FromResult(AuthenticateResult.Fail("Session expirée ou révoquée."));
        Context.Items[TokenItem] = token;
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "owner"), new Claim(ClaimTypes.Name, "owner"),
            new Claim("transport", bearer ? "bearer" : "cookie")
        ], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
