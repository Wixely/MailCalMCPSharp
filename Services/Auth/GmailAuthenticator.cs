using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Services.Models;

namespace MailCalMCPSharp.Services.Auth;

/// <summary>
/// Google (Gmail + Calendar) authenticator. Interactive sign-in uses the loopback browser flow
/// (<see cref="GoogleWebAuthorizationBroker"/>); device-code uses Google's OAuth device endpoint.
/// Tokens are stored via <see cref="GoogleTokenDataStore"/> in the shared exe-adjacent folder,
/// and silent refresh runs through a <see cref="UserCredential"/>.
/// </summary>
public sealed class GmailAuthenticator : AuthenticatorBase
{
    private const string UserId = "user";
    private const string DeviceCodeEndpoint = "https://oauth2.googleapis.com/device/code";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string DeviceGrantType = "urn:ietf:params:oauth:grant-type:device_code";

    private static readonly HttpClient Http = new();

    private readonly GoogleTokenDataStore _dataStore;
    private readonly Lazy<GoogleAuthorizationCodeFlow> _flow;
    private readonly string _clientId;
    private readonly string _clientSecret;

    // Background device-code completion when authorizing via the MCP tool (non-blocking).
    private Task? _pendingDeviceCode;

    public GmailAuthenticator(AccountEntry entry, string tokenDirectory, string? encryptionKey, MailCalOptions options)
        : base(entry, options)
    {
        _dataStore = new GoogleTokenDataStore(tokenDirectory, encryptionKey, entry.Alias);
        _clientId = MailCalMCPSharp.Services.AccountRegistry.ResolveSecret(entry.ClientId);
        _clientSecret = MailCalMCPSharp.Services.AccountRegistry.ResolveSecret(entry.ClientSecret);
        _flow = new Lazy<GoogleAuthorizationCodeFlow>(BuildFlow);
    }

    protected override IReadOnlyList<string> Scopes { get; } = new[]
    {
        "https://www.googleapis.com/auth/gmail.modify",
        "https://www.googleapis.com/auth/gmail.send",
        "https://www.googleapis.com/auth/gmail.settings.basic", // v2 filters (rules)
        "https://www.googleapis.com/auth/calendar",
        "https://www.googleapis.com/auth/contacts",             // v2 contacts (People API)
    };

    protected override string? MissingConfigReason()
    {
        if (string.IsNullOrWhiteSpace(_clientId))
        {
            return "ClientId is not configured.";
        }
        if (string.IsNullOrWhiteSpace(_clientSecret))
        {
            return "ClientSecret is not configured (required for Google OAuth clients).";
        }
        return null;
    }

    protected override bool HasStoredToken() => _dataStore.HasAny();

    private GoogleAuthorizationCodeFlow BuildFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets { ClientId = _clientId, ClientSecret = _clientSecret },
        Scopes = Scopes,
        DataStore = _dataStore,
    });

    /// <summary>Load the stored credential (used by GmailAccount to build Gmail/Calendar services).</summary>
    public async Task<UserCredential> GetCredentialAsync(CancellationToken ct)
    {
        var token = await _flow.Value.LoadTokenAsync(UserId, ct).ConfigureAwait(false);
        if (token is null)
        {
            throw NotAuthorized();
        }
        return new UserCredential(_flow.Value, UserId, token);
    }

    public override async Task<string> AcquireAccessTokenAsync(CancellationToken ct)
    {
        var credential = await GetCredentialAsync(ct).ConfigureAwait(false);
        var token = await credential.GetAccessTokenForRequestAsync(cancellationToken: ct).ConfigureAwait(false);
        return token ?? throw NotAuthorized();
    }

    public override async Task<AuthorizeResult> AuthorizeInteractiveAsync(CancellationToken ct)
    {
        await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets { ClientId = _clientId, ClientSecret = _clientSecret },
            Scopes,
            UserId,
            ct,
            _dataStore).ConfigureAwait(false);
        return Authorized("Signed in with Google.");
    }

    public override async Task<AuthorizeResult> AuthorizeDeviceCodeAsync(bool waitForCompletion, CancellationToken ct)
    {
        var device = await RequestDeviceCodeAsync(ct).ConfigureAwait(false);

        if (waitForCompletion)
        {
            await PollForTokenAsync(device, CancellationToken.None).ConfigureAwait(false);
            return Authorized("Signed in with Google (device code).");
        }

        _pendingDeviceCode = Task.Run(() => PollForTokenAsync(device, CancellationToken.None));
        _ = _pendingDeviceCode.ContinueWith(static _ => { }, TaskScheduler.Default);

        return new AuthorizeResult
        {
            Account = Entry.Alias,
            State = AuthState.Authorizing,
            Completed = false,
            VerificationUrl = device.VerificationUrl,
            UserCode = device.UserCode,
            ExpiresInSeconds = device.ExpiresInSeconds,
            Message = $"Open {device.VerificationUrl} and enter code {device.UserCode}, then check mailcal_auth_status.",
        };
    }

    public override async Task<bool> SignOutAsync(CancellationToken ct)
    {
        var had = _dataStore.HasAny();
        await _dataStore.ClearAsync().ConfigureAwait(false);
        return had;
    }

    private async Task<DeviceCodeInfo> RequestDeviceCodeAsync(CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["scope"] = string.Join(' ', Scopes),
        });
        using var response = await Http.PostAsync(DeviceCodeEndpoint, content, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Google device-code request failed ({(int)response.StatusCode}). The OAuth client may not be a " +
                $"'TV and Limited Input' client, which device flow requires. Response: {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var url = root.TryGetProperty("verification_url", out var u) ? u.GetString()
            : root.TryGetProperty("verification_uri", out var u2) ? u2.GetString() : null;
        return new DeviceCodeInfo(
            root.GetProperty("device_code").GetString()!,
            root.GetProperty("user_code").GetString()!,
            url ?? "https://www.google.com/device",
            root.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5,
            root.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 1800);
    }

    private async Task PollForTokenAsync(DeviceCodeInfo device, CancellationToken ct)
    {
        var interval = Math.Max(1, device.IntervalSeconds);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(device.ExpiresInSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct).ConfigureAwait(false);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
                ["device_code"] = device.DeviceCode,
                ["grant_type"] = DeviceGrantType,
            });
            using var response = await Http.PostAsync(TokenEndpoint, content, ct).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (response.IsSuccessStatusCode)
            {
                var token = new TokenResponse
                {
                    AccessToken = root.GetProperty("access_token").GetString(),
                    RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
                    ExpiresInSeconds = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt64() : 3600,
                    Scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() : string.Join(' ', Scopes),
                    TokenType = root.TryGetProperty("token_type", out var tt) ? tt.GetString() : "Bearer",
                    IssuedUtc = DateTime.UtcNow,
                };
                await _dataStore.StoreAsync(UserId, token).ConfigureAwait(false);
                return;
            }

            var error = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown_error";
            switch (error)
            {
                case "authorization_pending":
                    break;
                case "slow_down":
                    interval += 5;
                    break;
                default:
                    throw new InvalidOperationException($"Google device authorization failed: {error}.");
            }
        }

        throw new InvalidOperationException("Google device authorization timed out before the code was approved.");
    }

    private InvalidOperationException NotAuthorized() => new(
        $"Account '{Entry.Alias}' is not authorized (NeedsAuthorization). Call mailcal_authorize(account='{Entry.Alias}').");

    private sealed record DeviceCodeInfo(string DeviceCode, string UserCode, string VerificationUrl, int IntervalSeconds, int ExpiresInSeconds);
}
