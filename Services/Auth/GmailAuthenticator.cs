using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Services.Models;

namespace MailCalMCPSharp.Services.Auth;

/// <summary>
/// Google (Gmail + Calendar) authenticator. v1 build wires this to
/// <c>GoogleWebAuthorizationBroker</c> (loopback) / device flow with a <c>FileDataStore</c>
/// pointed at <see cref="ITokenStore"/>, plus silent refresh via <c>UserCredential</c>.
/// </summary>
public sealed class GmailAuthenticator : AuthenticatorBase
{
    public GmailAuthenticator(AccountEntry entry, ITokenStore store, MailCalOptions options)
        : base(entry, store, options)
    {
    }

    protected override IReadOnlyList<string> Scopes { get; } = new[]
    {
        "https://www.googleapis.com/auth/gmail.modify",
        "https://www.googleapis.com/auth/gmail.send",
        "https://www.googleapis.com/auth/calendar",
        "https://www.googleapis.com/auth/contacts",           // v2
        "https://www.googleapis.com/auth/gmail.settings.basic", // v2 (filters)
    };

    // Google web clients need a client secret in addition to the client id.
    protected override string? MissingConfigReason()
    {
        if (string.IsNullOrWhiteSpace(Entry.ClientId))
        {
            return "ClientId is not configured.";
        }

        if (string.IsNullOrWhiteSpace(Entry.ClientSecret))
        {
            return "ClientSecret is not configured (required for Google OAuth clients).";
        }

        return null;
    }

    public override Task<AuthorizeResult> AuthorizeInteractiveAsync(CancellationToken ct) =>
        Task.FromResult(NotImplementedAuthorize("interactive"));

    public override Task<AuthorizeResult> AuthorizeDeviceCodeAsync(CancellationToken ct) =>
        Task.FromResult(NotImplementedAuthorize("device-code"));

    public override Task<string> AcquireAccessTokenAsync(CancellationToken ct) =>
        throw new NotImplementedException("Gmail silent token acquisition is not implemented in the v1 skeleton yet.");
}
