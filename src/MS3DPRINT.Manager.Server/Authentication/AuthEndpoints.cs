using Microsoft.AspNetCore.Antiforgery;

namespace MS3DPRINT.Manager.Server.Authentication;

public static class AuthEndpoints
{
    public static void MapAuthentication(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth");
        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new CsrfResponse(antiforgery.GetAndStoreTokens(context).RequestToken!)));
        group.MapPost("/login", Login).RequireRateLimiting("login");
        group.MapGet("/session", () => Results.Ok(new SessionResponse(true))).RequireAuthorization();
        group.MapPost("/logout", Logout).RequireAuthorization();
    }

    private static async Task<IResult> Login(LoginRequest request, HttpContext context, IAntiforgery antiforgery, SessionStore sessions, TimeProvider clock)
    {
        var native = context.Request.Headers["X-MS3DFORGE-Client"] == "native";
        if (native)
        {
            if (context.Request.Headers.ContainsKey("Origin") || context.Request.Cookies.ContainsKey(SessionAuthenticationHandler.CookieName))
                return Results.BadRequest();
        }
        else if (!await CheckCsrf(context, antiforgery)) return Results.BadRequest();
        if (request.Name is null || request.Password is null || !sessions.VerifyOwner(request.Name, request.Password))
            return Results.Unauthorized();
        var token = sessions.CreateSession(native ? SessionStore.NativeLifetime : SessionStore.Lifetime);
        if (native) return Results.Ok(new NativeSessionResponse(token, (int)SessionStore.NativeLifetime.TotalSeconds));
        context.Response.Cookies.Append(SessionAuthenticationHandler.CookieName, token, Cookie(clock.GetUtcNow() + SessionStore.Lifetime));
        return Results.Ok(new SessionResponse(true));
    }

    private static async Task<IResult> Logout(HttpContext context, IAntiforgery antiforgery, SessionStore sessions)
    {
        if (context.User.FindFirst("transport")?.Value == "cookie" && !await CheckCsrf(context, antiforgery))
            return Results.BadRequest();
        sessions.Revoke((string)context.Items[SessionAuthenticationHandler.TokenItem]!);
        context.Response.Cookies.Delete(SessionAuthenticationHandler.CookieName, Cookie(null));
        return Results.NoContent();
    }

    private static async Task<bool> CheckCsrf(HttpContext context, IAntiforgery antiforgery)
    {
        try { await antiforgery.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }

    private static CookieOptions Cookie(DateTimeOffset? expires) => new()
    {
        Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true, Expires = expires
    };
}
