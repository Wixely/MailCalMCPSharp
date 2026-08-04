using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Services.Providers;

namespace MailCalMCPSharp.Services.Models;

/// <summary>
/// Auth lifecycle state for one account. Always observable via <c>mailcal_auth_status</c> so
/// the agent never has to guess whether a provider is ready.
/// </summary>
public enum AuthState
{
    /// <summary>No usable credentials configured (e.g. missing ClientId).</summary>
    NotConfigured,

    /// <summary>Configured, but no valid token on disk — call <c>mailcal_authorize</c>.</summary>
    NeedsAuthorization,

    /// <summary>An authorization flow is currently in progress.</summary>
    Authorizing,

    /// <summary>A valid (refreshable) token is present; the account is usable.</summary>
    Authorized,

    /// <summary>Token exists but is revoked/expired-beyond-refresh, or the last refresh failed.</summary>
    Error,
}

/// <summary>Per-account auth status returned to the agent. Never carries token material.</summary>
public sealed record AuthStatus
{
    public required string Account { get; init; }
    public required MailCalProvider Provider { get; init; }
    public required AuthState State { get; init; }

    /// <summary>Human-oriented next step, e.g. "Call mailcal_authorize(account='work')".</summary>
    public string? NextAction { get; init; }

    /// <summary>Scopes granted on the stored token, if known.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>Access-token expiry, if known (the refresh token outlives this).</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Populated when <see cref="State"/> is <see cref="AuthState.Error"/>.</summary>
    public string? Detail { get; init; }
}

/// <summary>Account summary for <c>mailcal_list_accounts</c>. No secrets.</summary>
public sealed record AccountSummary
{
    public required string Alias { get; init; }
    public required MailCalProvider Provider { get; init; }
    public bool IsDefault { get; init; }
    public string? Description { get; init; }
    public required AuthState AuthState { get; init; }
    public required ProviderCapabilities Capabilities { get; init; }
}

/// <summary>
/// Result of an interactive/device-code authorize call. For device-code, the URL + user code
/// are relayed to the agent; for the browser flow, <see cref="Completed"/> is typically true.
/// </summary>
public sealed record AuthorizeResult
{
    public required string Account { get; init; }
    public required AuthState State { get; init; }
    public bool Completed { get; init; }

    /// <summary>Device-code: verification URL the user opens.</summary>
    public string? VerificationUrl { get; init; }

    /// <summary>Device-code: short code the user enters.</summary>
    public string? UserCode { get; init; }

    /// <summary>Device-code: seconds until the code expires.</summary>
    public int? ExpiresInSeconds { get; init; }

    /// <summary>Human-oriented message describing what happened / what to do next.</summary>
    public string? Message { get; init; }
}
