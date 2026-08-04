using MailCalMCPSharp.Services.Models;

namespace MailCalMCPSharp.Services.Auth;

/// <summary>
/// Provider-neutral auth contract, one instance per account. The auth *tools*
/// (<c>mailcal_authorize</c> / <c>_deauthorize</c> / <c>_auth_status</c>) and the
/// <c>--auth</c> CLI branch both drive this — so a provider's OAuth quirks stay in one place.
/// </summary>
public interface IAuthenticator
{
    /// <summary>Current auth state (config + stored-token check). Never returns token material.</summary>
    Task<AuthStatus> GetStatusAsync(CancellationToken ct);

    /// <summary>Interactive browser (loopback) sign-in. Opens the system browser and awaits consent.</summary>
    Task<AuthorizeResult> AuthorizeInteractiveAsync(CancellationToken ct);

    /// <summary>
    /// Device-code sign-in for machines without a browser. When <paramref name="waitForCompletion"/>
    /// is false (the MCP tool path) it returns the URL + user code immediately and finishes the
    /// flow in the background; when true (the <c>--auth</c> CLI path) it blocks until the user
    /// completes sign-in.
    /// </summary>
    Task<AuthorizeResult> AuthorizeDeviceCodeAsync(bool waitForCompletion, CancellationToken ct);

    /// <summary>Delete the stored token, reverting the account to <see cref="AuthState.NeedsAuthorization"/>.</summary>
    Task<bool> SignOutAsync(CancellationToken ct);

    /// <summary>
    /// Silently acquire an access token for API calls (refreshing as needed). Throws if the
    /// account is not authorized — callers should surface "run mailcal_authorize" to the agent.
    /// </summary>
    Task<string> AcquireAccessTokenAsync(CancellationToken ct);
}
