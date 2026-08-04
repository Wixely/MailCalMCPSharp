using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Services.Models;

namespace MailCalMCPSharp.Services.Auth;

/// <summary>
/// Authenticator for generic IMAP/SMTP accounts. These use configured username/password
/// credentials rather than OAuth, so there is no interactive/device-code flow and no token
/// store — "authorized" simply means the connection settings are present. The authorize tools
/// still work uniformly and return a clear explanation.
/// </summary>
public sealed class ImapAuthenticator : AuthenticatorBase
{
    public ImapAuthenticator(AccountEntry entry, MailCalOptions options) : base(entry, options)
    {
    }

    protected override IReadOnlyList<string> Scopes { get; } = Array.Empty<string>();

    protected override string? MissingConfigReason()
    {
        var s = Entry.Imap;
        if (string.IsNullOrWhiteSpace(s.ImapHost)) return "IMAP host is not configured.";
        if (string.IsNullOrWhiteSpace(s.SmtpHost)) return "SMTP host is not configured.";
        if (string.IsNullOrWhiteSpace(s.Username)) return "Username is not configured.";
        if (string.IsNullOrWhiteSpace(MailCalMCPSharp.Services.AccountRegistry.ResolveSecret(s.Password))) return "Password is not configured.";
        return null;
    }

    // Credentials are the auth: if configured, the account is usable.
    protected override bool HasStoredToken() => MissingConfigReason() is null;

    public override Task<AuthorizeResult> AuthorizeInteractiveAsync(CancellationToken ct) => Task.FromResult(NoAuthNeeded());

    public override Task<AuthorizeResult> AuthorizeDeviceCodeAsync(bool waitForCompletion, CancellationToken ct) => Task.FromResult(NoAuthNeeded());

    public override Task<bool> SignOutAsync(CancellationToken ct) => Task.FromResult(false);

    public override Task<string> AcquireAccessTokenAsync(CancellationToken ct) =>
        throw new NotSupportedException("IMAP/SMTP accounts authenticate with username/password, not access tokens.");

    private AuthorizeResult NoAuthNeeded()
    {
        var missing = MissingConfigReason();
        return new AuthorizeResult
        {
            Account = Entry.Alias,
            State = missing is null ? AuthState.Authorized : AuthState.NotConfigured,
            Completed = missing is null,
            Message = missing is null
                ? "IMAP/SMTP account uses configured credentials — no OAuth authorization is required."
                : $"IMAP/SMTP account is not fully configured: {missing}",
        };
    }
}
