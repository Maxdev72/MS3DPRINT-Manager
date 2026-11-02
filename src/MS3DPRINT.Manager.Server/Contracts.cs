using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Server;

/// <summary>Credentials supplied over HTTPS for a private owner session.</summary>
public sealed record LoginRequest(string? Name, string? Password);

/// <summary>Antiforgery request token to send in X-CSRF-TOKEN with browser mutations.</summary>
public sealed record CsrfResponse(string RequestToken);

/// <summary>Short-lived session returned only to a native client.</summary>
public sealed record NativeSessionResponse(string AccessToken, int ExpiresIn);

/// <summary>Confirms the current session without returning credentials or token.</summary>
public sealed record SessionResponse(bool Authenticated);

/// <summary>Client summary with a concurrency version and no server filesystem path.</summary>
public sealed record ClientResponse(Guid Id, string Kind, string ClientCode, string DisplayName, DateTimeOffset UpdatedAt);

/// <summary>Project summary and ordered tasks; UpdatedAt is the version for future commands.</summary>
public sealed record ProjectResponse(Guid Id, Guid ClientId, string Reference, string ProjectName, string Status,
    DateOnly? DueDate, string Priority, DateTimeOffset UpdatedAt, IReadOnlyList<ProjectTask> Tasks);
