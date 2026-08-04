using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Services.Auth;
using MailCalMCPSharp.Services.Models;
using MailCalMCPSharp.Services.Providers;
using MailCalMCPSharp.Services.Providers.Gmail;
using MailCalMCPSharp.Services.Providers.Outlook;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MailCalMCPSharp.Services;

/// <summary>
/// Owns every configured account across every provider. Tools call <see cref="Resolve"/> with an
/// optional alias to get a provider-neutral <see cref="IMailCalAccount"/>, and route auth through
/// <see cref="Authenticator"/>. Safety gates (read-only, permanent-delete, feature toggles) live
/// here so tools stay thin. Aliases missing in config are auto-named <c>&lt;provider&gt;-N</c>.
/// </summary>
public sealed class AccountRegistry
{
    private readonly MailCalOptions _options;
    private readonly ITokenStore _tokenStore;
    private readonly string _tokenDirectory;
    private readonly Dictionary<string, IMailCalAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IAuthenticator> _authenticators = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _defaultAlias;

    public AccountRegistry(IOptions<MailCalOptions> options, IHostEnvironment env)
    {
        _options = options.Value;

        // Resolve the token store to a folder adjacent to the executable (content root), even for
        // a Windows Service. Absolute paths are honoured as-is (e.g. a Docker volume mount).
        var dir = string.IsNullOrWhiteSpace(_options.TokenStoreDirectory) ? "tokens" : _options.TokenStoreDirectory;
        _tokenDirectory = Path.IsPathRooted(dir) ? dir : Path.Combine(env.ContentRootPath, dir);
        var encryptionKey = ResolveSecret(_options.TokenEncryptionKey);
        _tokenStore = new FileTokenStore(_tokenDirectory, encryptionKey);

        var counters = new Dictionary<MailCalProvider, int>();
        foreach (var entry in _options.Accounts)
        {
            var alias = entry.Alias;
            if (string.IsNullOrWhiteSpace(alias))
            {
                var n = counters.TryGetValue(entry.Provider, out var c) ? c + 1 : 1;
                counters[entry.Provider] = n;
                alias = $"{entry.Provider.ToString().ToLowerInvariant()}-{n}";
            }
            while (_accounts.ContainsKey(alias))
            {
                var n = counters.TryGetValue(entry.Provider, out var c) ? c + 1 : 1;
                counters[entry.Provider] = n;
                alias = $"{entry.Provider.ToString().ToLowerInvariant()}-{n}";
            }
            entry.Alias = alias; // reflect resolved alias back so listings read cleanly

            IAuthenticator authenticator;
            IMailCalAccount account;
            switch (entry.Provider)
            {
                case MailCalProvider.Outlook:
                    var outlookAuth = new OutlookAuthenticator(entry, _tokenStore, _options);
                    authenticator = outlookAuth;
                    account = new OutlookAccount(entry, outlookAuth, _options);
                    break;
                case MailCalProvider.Gmail:
                    var gmailAuth = new GmailAuthenticator(entry, _tokenDirectory, encryptionKey, _options);
                    authenticator = gmailAuth;
                    account = new GmailAccount(entry, gmailAuth, _options);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(entry.Provider), entry.Provider, "Unknown provider.");
            }

            _accounts[alias] = account;
            _authenticators[alias] = authenticator;
        }

        var preferred = _options.DefaultAccount;
        _defaultAlias = !string.IsNullOrWhiteSpace(preferred) && _accounts.ContainsKey(preferred)
            ? preferred
            : _accounts.Keys.FirstOrDefault();
    }

    public MailCalOptions Options => _options;
    public bool IsReadOnly => _options.ReadOnly;
    public bool AllowPermanentDelete => _options.AllowPermanentDelete;
    public IReadOnlyCollection<string> Aliases => _accounts.Keys;
    public string? DefaultAlias => _defaultAlias;

    /// <summary>Absolute path to the exe-adjacent token store folder (logged at startup).</summary>
    public string TokenDirectory => _tokenDirectory;

    /// <summary>Resolve an account by alias, or the default when alias is blank.</summary>
    public IMailCalAccount Resolve(string? alias)
    {
        var key = ResolveAliasKey(alias);
        return _accounts[key];
    }

    /// <summary>Resolve the authenticator for an account by alias, or the default when alias is blank.</summary>
    public IAuthenticator Authenticator(string? alias)
    {
        var key = ResolveAliasKey(alias);
        return _authenticators[key];
    }

    private string ResolveAliasKey(string? alias)
    {
        if (_accounts.Count == 0)
        {
            throw new InvalidOperationException("No accounts are configured. Add entries under MailCal:Accounts.");
        }

        if (!string.IsNullOrWhiteSpace(alias))
        {
            if (_accounts.ContainsKey(alias))
            {
                return alias;
            }
            throw new InvalidOperationException(
                $"Unknown account '{alias}'. Available: {string.Join(", ", _accounts.Keys)}.");
        }

        return _defaultAlias
            ?? throw new InvalidOperationException("No default account is configured.");
    }

    public async Task<IReadOnlyList<AccountSummary>> ListAccountsAsync(CancellationToken ct)
    {
        var list = new List<AccountSummary>(_accounts.Count);
        foreach (var (alias, account) in _accounts)
        {
            var status = await _authenticators[alias].GetStatusAsync(ct).ConfigureAwait(false);
            var entry = _options.Accounts.FirstOrDefault(e => string.Equals(e.Alias, alias, StringComparison.OrdinalIgnoreCase));
            list.Add(new AccountSummary
            {
                Alias = alias,
                Provider = account.Provider,
                IsDefault = string.Equals(alias, _defaultAlias, StringComparison.OrdinalIgnoreCase),
                Description = string.IsNullOrWhiteSpace(entry?.Description) ? null : entry!.Description,
                AuthState = status.State,
                Capabilities = account.Capabilities,
            });
        }
        return list;
    }

    // ---- Safety gates ----

    public void EnsureWriteAllowed(string operation)
    {
        if (_options.ReadOnly)
        {
            throw new InvalidOperationException(
                $"Operation '{operation}' is blocked: server is running in read-only mode (MailCal:ReadOnly=true).");
        }
    }

    public void EnsurePermanentDeleteAllowed(string operation)
    {
        if (!_options.AllowPermanentDelete)
        {
            throw new InvalidOperationException(
                $"Operation '{operation}' requires MailCal:ReadOnly=false and MailCal:AllowPermanentDelete=true. " +
                "Soft delete (move to trash) is available without the second switch.");
        }
    }

    public void EnsureMailEnabled()
    {
        if (!_options.EnableMail)
        {
            throw new InvalidOperationException("Mail tools are disabled (MailCal:EnableMail=false).");
        }
    }

    public void EnsureCalendarEnabled()
    {
        if (!_options.EnableCalendar)
        {
            throw new InvalidOperationException("Calendar tools are disabled (MailCal:EnableCalendar=false).");
        }
    }

    /// <summary>Reports "not supported by &lt;provider&gt;" when an account lacks a capability.</summary>
    public static void EnsureCapability(IMailCalAccount account, bool capable, string feature)
    {
        if (!capable)
        {
            throw new InvalidOperationException(
                $"{account.Provider} does not support {feature} for account '{account.Alias}'.");
        }
    }

    /// <summary>Resolve a <c>file:</c>-prefixed secret to file contents; pass through otherwise.</summary>
    public static string ResolveSecret(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var path = value["file:".Length..];
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }

        return value;
    }
}
