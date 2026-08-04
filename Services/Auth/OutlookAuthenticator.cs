using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Services.Models;
using Microsoft.Identity.Client;

namespace MailCalMCPSharp.Services.Auth;

/// <summary>
/// Microsoft Graph / MSAL authenticator. The MSAL token cache is persisted into the shared
/// exe-adjacent token store (<see cref="ITokenStore"/>) under the account alias, so refresh
/// tokens live next to the executable and survive restarts.
/// </summary>
public sealed class OutlookAuthenticator : AuthenticatorBase
{
    private readonly ITokenStore _store;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly Lazy<IPublicClientApplication> _app;

    // Background device-code completion when authorizing via the MCP tool (non-blocking).
    private Task<AuthenticationResult>? _pendingDeviceCode;

    public OutlookAuthenticator(AccountEntry entry, ITokenStore store, MailCalOptions options)
        : base(entry, options)
    {
        _store = store;
        _app = new Lazy<IPublicClientApplication>(BuildApp);
    }

    protected override IReadOnlyList<string> Scopes { get; } = new[]
    {
        "https://graph.microsoft.com/Mail.ReadWrite",
        "https://graph.microsoft.com/Mail.Send",
        "https://graph.microsoft.com/Calendars.ReadWrite",
        "https://graph.microsoft.com/Contacts.ReadWrite",       // v2 contacts
        "https://graph.microsoft.com/MailboxSettings.ReadWrite", // v2 inbox rules
    };

    protected override bool HasStoredToken() => _store.Exists(Entry.Alias);

    private IPublicClientApplication BuildApp()
    {
        var tenant = string.IsNullOrWhiteSpace(Entry.TenantId) ? "common" : Entry.TenantId;
        var app = PublicClientApplicationBuilder.Create(Entry.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{tenant}")
            .WithRedirectUri("http://localhost")
            .Build();

        app.UserTokenCache.SetBeforeAccessAsync(async args =>
        {
            await _cacheLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var bytes = await _store.LoadAsync(Entry.Alias, CancellationToken.None).ConfigureAwait(false);
                if (bytes is { Length: > 0 })
                {
                    args.TokenCache.DeserializeMsalV3(bytes);
                }
            }
            finally
            {
                _cacheLock.Release();
            }
        });

        app.UserTokenCache.SetAfterAccessAsync(async args =>
        {
            if (!args.HasStateChanged)
            {
                return;
            }

            await _cacheLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var bytes = args.TokenCache.SerializeMsalV3();
                await _store.SaveAsync(Entry.Alias, bytes, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _cacheLock.Release();
            }
        });

        return app;
    }

    public override async Task<string> AcquireAccessTokenAsync(CancellationToken ct)
    {
        var app = _app.Value;
        var accounts = await app.GetAccountsAsync().ConfigureAwait(false);
        var account = accounts.FirstOrDefault();
        if (account is null)
        {
            throw NotAuthorized();
        }

        try
        {
            var result = await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(ct).ConfigureAwait(false);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            throw NotAuthorized();
        }
    }

    public override async Task<AuthorizeResult> AuthorizeInteractiveAsync(CancellationToken ct)
    {
        var app = _app.Value;
        var result = await app.AcquireTokenInteractive(Scopes)
            .WithPrompt(Prompt.SelectAccount)
            .ExecuteAsync(ct)
            .ConfigureAwait(false);
        return Authorized($"Signed in as {result.Account?.Username}.");
    }

    public override async Task<AuthorizeResult> AuthorizeDeviceCodeAsync(bool waitForCompletion, CancellationToken ct)
    {
        var app = _app.Value;
        var codeTcs = new TaskCompletionSource<DeviceCodeResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var acquisition = app.AcquireTokenWithDeviceCode(Scopes, dcr =>
        {
            codeTcs.TrySetResult(dcr);
            return Task.CompletedTask;
        }).ExecuteAsync(ct);

        if (waitForCompletion)
        {
            var result = await acquisition.ConfigureAwait(false);
            return Authorized($"Signed in as {result.Account?.Username}.");
        }

        var first = await Task.WhenAny(codeTcs.Task, acquisition).ConfigureAwait(false);
        if (first == acquisition)
        {
            // Completed or faulted before the code callback fired.
            var result = await acquisition.ConfigureAwait(false);
            return Authorized($"Signed in as {result.Account?.Username}.");
        }

        var code = await codeTcs.Task.ConfigureAwait(false);
        _pendingDeviceCode = acquisition;
        _ = acquisition.ContinueWith(static _ => { }, TaskScheduler.Default);

        return new AuthorizeResult
        {
            Account = Entry.Alias,
            State = AuthState.Authorizing,
            Completed = false,
            VerificationUrl = code.VerificationUrl,
            UserCode = code.UserCode,
            ExpiresInSeconds = (int)Math.Max(0, (code.ExpiresOn - DateTimeOffset.UtcNow).TotalSeconds),
            Message = $"Open {code.VerificationUrl} and enter code {code.UserCode}, then check mailcal_auth_status.",
        };
    }

    public override async Task<bool> SignOutAsync(CancellationToken ct)
    {
        var app = _app.Value;
        var accounts = await app.GetAccountsAsync().ConfigureAwait(false);
        foreach (var account in accounts)
        {
            await app.RemoveAsync(account).ConfigureAwait(false);
        }
        return _store.Delete(Entry.Alias);
    }

    private InvalidOperationException NotAuthorized() => new(
        $"Account '{Entry.Alias}' is not authorized (NeedsAuthorization). Call mailcal_authorize(account='{Entry.Alias}').");
}
