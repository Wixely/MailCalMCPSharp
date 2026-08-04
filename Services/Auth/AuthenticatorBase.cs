using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Services.Models;

namespace MailCalMCPSharp.Services.Auth;

/// <summary>
/// Shared authenticator behaviour: config validation and observable auth state. Providers supply
/// the OAuth-specific bits (scopes, stored-token detection, interactive/device-code acquisition,
/// silent refresh, sign-out).
/// </summary>
public abstract class AuthenticatorBase : IAuthenticator
{
    protected AuthenticatorBase(AccountEntry entry, MailCalOptions options)
    {
        Entry = entry;
        Options = options;
    }

    protected AccountEntry Entry { get; }
    protected MailCalOptions Options { get; }

    /// <summary>OAuth scopes requested for this provider.</summary>
    protected abstract IReadOnlyList<string> Scopes { get; }

    /// <summary>True if a usable token appears to be stored for this account.</summary>
    protected abstract bool HasStoredToken();

    /// <summary>Provider-specific reason the account is <see cref="AuthState.NotConfigured"/>, or null if configured.</summary>
    protected virtual string? MissingConfigReason() =>
        string.IsNullOrWhiteSpace(Entry.ClientId) ? "ClientId is not configured." : null;

    public virtual Task<AuthStatus> GetStatusAsync(CancellationToken ct)
    {
        var missing = MissingConfigReason();
        AuthState state;
        string? next = null;

        if (missing is not null)
        {
            state = AuthState.NotConfigured;
            next = missing + $" Configure account '{Entry.Alias}' under MailCal:Accounts.";
        }
        else if (HasStoredToken())
        {
            // Optimistic: a stored token implies authorized. Operations surface real refresh
            // failures; auth_status stays fast and does not hit the network on every call.
            state = AuthState.Authorized;
        }
        else
        {
            state = AuthState.NeedsAuthorization;
            next = $"Call mailcal_authorize(account='{Entry.Alias}') to sign in.";
        }

        return Task.FromResult(new AuthStatus
        {
            Account = Entry.Alias,
            Provider = Entry.Provider,
            State = state,
            NextAction = next,
            Scopes = state == AuthState.Authorized ? Scopes : Array.Empty<string>(),
        });
    }

    public abstract Task<AuthorizeResult> AuthorizeInteractiveAsync(CancellationToken ct);

    public abstract Task<AuthorizeResult> AuthorizeDeviceCodeAsync(bool waitForCompletion, CancellationToken ct);

    public abstract Task<bool> SignOutAsync(CancellationToken ct);

    public abstract Task<string> AcquireAccessTokenAsync(CancellationToken ct);

    protected AuthorizeResult Authorized(string? message) => new()
    {
        Account = Entry.Alias,
        State = AuthState.Authorized,
        Completed = true,
        Message = message,
    };
}
