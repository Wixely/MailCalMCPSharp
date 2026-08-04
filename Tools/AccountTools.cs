using System.ComponentModel;
using System.Text.Json;
using MailCalMCPSharp.Services;
using ModelContextProtocol.Server;

namespace MailCalMCPSharp.Tools;

/// <summary>
/// Cross-cutting account and OAuth lifecycle tools. Auth is agent-driven: the agent can list
/// accounts, inspect auth state, start a sign-in (browser or device-code), and sign out.
/// </summary>
[McpServerToolType]
public sealed class AccountTools
{
    [McpServerTool(Name = "mailcal_list_accounts"),
     Description("List configured mail/calendar accounts with provider, default flag, capabilities, and auth state. No secrets are returned.")]
    public static async Task<string> ListAccounts(AccountRegistry svc, CancellationToken ct = default)
    {
        var accounts = await svc.ListAccountsAsync(ct);
        return JsonSerializer.Serialize(new { defaultAccount = svc.DefaultAlias, accounts }, JsonOpts.Default);
    }

    [McpServerTool(Name = "mailcal_auth_status"),
     Description("Report OAuth authorization state for one account (or all accounts if omitted), including the next action needed. Always available.")]
    public static async Task<string> AuthStatus(
        AccountRegistry svc,
        [Description("Account alias. Omit to report every configured account.")] string? account = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(account))
        {
            var status = await svc.Authenticator(account).GetStatusAsync(ct);
            return JsonSerializer.Serialize(status, JsonOpts.Default);
        }

        var all = new List<object>();
        foreach (var alias in svc.Aliases)
        {
            all.Add(await svc.Authenticator(alias).GetStatusAsync(ct));
        }
        return JsonSerializer.Serialize(all, JsonOpts.Default);
    }

    [McpServerTool(Name = "mailcal_authorize"),
     Description("Start OAuth sign-in for an account. mode 'browser' (default) opens the system browser on this machine; mode 'devicecode' returns a URL and code for the user to enter on another device. Blocked in read-only mode.")]
    public static async Task<string> Authorize(
        AccountRegistry svc,
        [Description("Account alias to authorize. Falls back to the default account.")] string? account = null,
        [Description("Sign-in mode: 'browser' (interactive, default) or 'devicecode'.")] string? mode = null,
        CancellationToken ct = default)
    {
        svc.EnsureWriteAllowed("mailcal_authorize");
        var authenticator = svc.Authenticator(account);
        // MCP path: device-code returns the URL+code immediately and completes in the background.
        var result = string.Equals(mode, "devicecode", StringComparison.OrdinalIgnoreCase)
            ? await authenticator.AuthorizeDeviceCodeAsync(waitForCompletion: false, ct)
            : await authenticator.AuthorizeInteractiveAsync(ct);
        return JsonSerializer.Serialize(result, JsonOpts.Default);
    }

    [McpServerTool(Name = "mailcal_deauthorize"),
     Description("Delete an account's stored OAuth token, reverting it to NeedsAuthorization. Blocked in read-only mode.")]
    public static async Task<string> Deauthorize(
        AccountRegistry svc,
        [Description("Account alias to sign out. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureWriteAllowed("mailcal_deauthorize");
        var authenticator = svc.Authenticator(account);
        var removed = await authenticator.SignOutAsync(ct);
        var status = await authenticator.GetStatusAsync(ct);
        return JsonSerializer.Serialize(new { removed, status }, JsonOpts.Default);
    }
}
