namespace MailCalMCPSharp.Configuration;

/// <summary>
/// Top-level MailCalMCPSharp config. One provider-agnostic tool surface
/// (<c>mail_* / cal_* / contact_*</c> plus cross-cutting <c>mailcal_*</c>) is routed to a
/// configured account by its <see cref="AccountEntry.Alias"/>. Each account names a
/// <see cref="AccountEntry.Provider"/> (Outlook | Gmail); adding a provider adds no tools.
///
/// v1 ships account/auth + mail + calendar. Contacts, email rules, and scheduled-send are
/// modelled (interfaces + toggles) but deferred to v2 — their toggles default off.
/// </summary>
public sealed class MailCalOptions
{
    public const string SectionName = "MailCal";

    /// <summary>
    /// Master safety switch. When true, every mutating tool (send, delete, move, create
    /// event, authorize/deauthorize, …) is refused; read tools stay available. Default true —
    /// flip explicitly per environment.
    /// </summary>
    public bool ReadOnly { get; set; } = true;

    /// <summary>
    /// Even with ReadOnly=false, permanent (hard) delete is gated behind this second switch.
    /// Default false — soft delete / move-to-trash is the default behaviour.
    /// </summary>
    public bool AllowPermanentDelete { get; set; } = false;

    /// <summary>Alias used when a tool omits its <c>account</c> argument. Falls back to the first configured account.</summary>
    public string? DefaultAccount { get; set; }

    /// <summary>Default page size for list operations.</summary>
    public int DefaultPageSize { get; set; } = 25;

    /// <summary>Maximum number of pages traversed for paginated list calls. Guards against runaway calls.</summary>
    public int MaxPages { get; set; } = 4;

    /// <summary>Maximum characters of a message body returned before truncation (with a <c>truncated</c> flag).</summary>
    public int MaxBodyChars { get; set; } = 20000;

    /// <summary>HTTP request timeout in seconds for provider API calls.</summary>
    public int RequestTimeoutSeconds { get; set; } = 100;

    /// <summary>
    /// Portable folder holding one token file per account alias. Relative paths resolve
    /// against the content root. Mountable as a Docker volume; copy-portable between machines.
    /// </summary>
    public string TokenStoreDirectory { get; set; } = "tokens";

    /// <summary>
    /// Blank = basic reversible encoding of token files (portable, no key to carry — convenience,
    /// not a security boundary). Set to a passphrase (or <c>file:</c> path) to AES-encrypt token
    /// files at rest; stays portable as long as the same key is present on the target machine.
    /// </summary>
    public string TokenEncryptionKey { get; set; } = string.Empty;

    /// <summary>Expose email tools.</summary>
    public bool EnableMail { get; set; } = true;

    /// <summary>Expose calendar tools.</summary>
    public bool EnableCalendar { get; set; } = true;

    /// <summary>Expose contact tools. (v2 — deferred.)</summary>
    public bool EnableContacts { get; set; } = false;

    /// <summary>Expose email rule / filter tools. (v2 — deferred.)</summary>
    public bool EnableRules { get; set; } = false;

    /// <summary>Expose scheduled-send tools where the provider supports it. (v2 — deferred.)</summary>
    public bool EnableScheduledSend { get; set; } = false;

    /// <summary>Configured mail/calendar accounts.</summary>
    public List<AccountEntry> Accounts { get; set; } = new();
}

/// <summary>Which backend an account talks to. Extend this enum to add providers.</summary>
public enum MailCalProvider
{
    Outlook,
    Gmail,
}

/// <summary>How an account authenticates. v1 uses delegated OAuth; the rest are reserved for v2+.</summary>
public enum AuthType
{
    /// <summary>Delegated OAuth 2.0 with a cached refresh token (interactive/device-code to obtain).</summary>
    Delegated,

    /// <summary>Reserved (v2): Graph app-only / client-credentials with application permissions.</summary>
    Application,

    /// <summary>Reserved (v2): Google Workspace service account with domain-wide delegation.</summary>
    ServiceAccount,
}

/// <summary>
/// One configured account. Credentials only — OAuth tokens live in the token store keyed by
/// <see cref="Alias"/>, never inline here.
/// </summary>
public sealed class AccountEntry
{
    /// <summary>Stable handle the agent uses to pick this account. Auto-generated (<c>&lt;provider&gt;-N</c>) when omitted.</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>Backend provider for this account.</summary>
    public MailCalProvider Provider { get; set; } = MailCalProvider.Outlook;

    /// <summary>Authentication mode. Default delegated OAuth.</summary>
    public AuthType AuthType { get; set; } = AuthType.Delegated;

    /// <summary>OAuth client (application) id registered with the provider.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth client secret. Not needed for public-client interactive Outlook flows; used by app-only and Google web clients. Supports a <c>file:</c> prefix.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Azure AD tenant id (Outlook). <c>common</c> for multi-tenant + consumer, or a specific tenant/organizations. Ignored by Gmail.</summary>
    public string TenantId { get; set; } = "common";

    /// <summary>Optional target mailbox UPN — used for app-only mailbox selection (v2). Ignored for delegated auth.</summary>
    public string? UserPrincipalName { get; set; }

    /// <summary>Free-text description surfaced by <c>mailcal_list_accounts</c>. Optional.</summary>
    public string Description { get; set; } = string.Empty;
}

public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5708;
    public string Path { get; set; } = "/mcp";

    /// <summary>Service name when running as a Windows Service.</summary>
    public string WindowsServiceName { get; set; } = "MailCalMCPSharp";

    /// <summary>Optional MCP endpoint password. Blank disables MCP password auth.</summary>
    public string Password { get; set; } = string.Empty;
}
