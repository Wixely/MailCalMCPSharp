using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Services.Models;

namespace MailCalMCPSharp.Services.Auth;

/// <summary>
/// Microsoft Graph / MSAL authenticator. v1 build wires this to
/// <c>PublicClientApplication</c> (interactive + device-code + silent) with the MSAL token
/// cache bound to <see cref="ITokenStore"/>.
/// </summary>
public sealed class OutlookAuthenticator : AuthenticatorBase
{
    public OutlookAuthenticator(AccountEntry entry, ITokenStore store, MailCalOptions options)
        : base(entry, store, options)
    {
    }

    protected override IReadOnlyList<string> Scopes { get; } = new[]
    {
        "Mail.ReadWrite",
        "Mail.Send",
        "Calendars.ReadWrite",
        "Contacts.ReadWrite",       // v2
        "MailboxSettings.ReadWrite", // v2 (rules)
        "offline_access",
    };

    public override Task<AuthorizeResult> AuthorizeInteractiveAsync(CancellationToken ct) =>
        Task.FromResult(NotImplementedAuthorize("interactive"));

    public override Task<AuthorizeResult> AuthorizeDeviceCodeAsync(CancellationToken ct) =>
        Task.FromResult(NotImplementedAuthorize("device-code"));

    public override Task<string> AcquireAccessTokenAsync(CancellationToken ct) =>
        throw new NotImplementedException("Outlook silent token acquisition is not implemented in the v1 skeleton yet.");
}
