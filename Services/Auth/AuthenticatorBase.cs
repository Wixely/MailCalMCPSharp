using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Services.Models;

namespace MailCalMCPSharp.Services.Auth;

/// <summary>
/// Shared authenticator behaviour: config validation, stored-token detection, and sign-out.
/// Providers supply the OAuth-specific bits (scopes, interactive/device-code acquisition, silent
/// refresh). The observable-state and sign-out paths work in the v1 skeleton; the acquisition
/// paths are implemented per provider in the v1 build.
/// </summary>
public abstract class AuthenticatorBase : IAuthenticator
{
    protected AuthenticatorBase(AccountEntry entry, ITokenStore store, MailCalOptions options)
    {
        Entry = entry;
        Store = store;
        Options = options;
    }

    protected AccountEntry Entry { get; }
    protected ITokenStore Store { get; }
    protected MailCalOptions Options { get; }

    /// <summary>OAuth scopes requested for this provider.</summary>
    protected abstract IReadOnlyList<string> Scopes { get; }

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
            next = missing + $" Set MailCal:Accounts:<{Entry.Alias}>:ClientId (and provider secret) to configure this account.";
        }
        else if (Store.Exists(Entry.Alias))
        {
            // Skeleton: presence of a stored token implies authorized. The v1 build validates and
            // downgrades to Error on a failed refresh.
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

    public abstract Task<AuthorizeResult> AuthorizeDeviceCodeAsync(CancellationToken ct);

    public virtual Task<bool> SignOutAsync(CancellationToken ct) => Task.FromResult(Store.Delete(Entry.Alias));

    public abstract Task<string> AcquireAccessTokenAsync(CancellationToken ct);

    /// <summary>Helper for stubbed provider acquisition paths.</summary>
    protected AuthorizeResult NotImplementedAuthorize(string mode) => throw new NotImplementedException(
        $"{Entry.Provider} {mode} authorization is not implemented in the v1 skeleton yet.");
}
