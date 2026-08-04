using Microsoft.Kiota.Abstractions.Authentication;

namespace MailCalMCPSharp.Services.Providers.Outlook;

/// <summary>
/// Kiota access-token provider that delegates to the account's authenticator for a fresh Graph
/// access token on each request (MSAL caches/refreshes underneath, so this is cheap).
/// </summary>
internal sealed class GraphTokenProvider : IAccessTokenProvider
{
    private readonly Func<CancellationToken, Task<string>> _acquire;

    public GraphTokenProvider(Func<CancellationToken, Task<string>> acquire) => _acquire = acquire;

    public AllowedHostsValidator AllowedHostsValidator { get; } = new(new[] { "graph.microsoft.com" });

    public Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
        => _acquire(cancellationToken);
}
