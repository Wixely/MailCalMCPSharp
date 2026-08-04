# MailCalMCPSharp

An MCP server for **email and calendar** across multiple providers — **Microsoft Outlook**
(via Microsoft Graph) and **Google Gmail / Calendar** (via the Google API client libraries) —
part of the MCPSharp product line.

One provider-agnostic tool surface is routed to a configured account by its alias, so the
same `mail_*` / `cal_*` tools work against Outlook or Gmail. Adding a provider in future adds
no new tools.

> **Status: v1 skeleton.** Hosting, configuration, safety gates, the account registry, the
> agent-driven auth lifecycle, and the full tool surface are wired up. The Outlook (Graph)
> and Gmail (Google) provider data calls and the OAuth token acquisition are stubbed and
> return a clear "not implemented in the v1 skeleton yet" message — they are the next build
> step. Contacts, email rules, and scheduled-send are planned for v2.

## Capabilities (v1)

- **Email:** list folders/labels, read, list, search, compose draft, send, delete
  (soft/trash by default), move.
- **Calendar:** list calendars, read a time window, get event, search, create, update,
  delete, respond to invites.
- **Accounts & auth:** list accounts, inspect auth state, authorize (browser or device-code),
  and sign out — all as tools the agent can call.

## Authentication

Delegated OAuth 2.0. Each account is authorized once; the server then runs unattended,
refreshing silently. Auth is **agent-driven** — the agent calls `mailcal_authorize` and the
server opens a browser on this machine (or returns a device code to relay). A one-time
`--auth` CLI mode is provided for the pure Windows-Service case.

Tokens are stored in a **portable folder** (`MailCal:TokenStoreDirectory`, default `tokens/`),
one file per account. By default they use a basic reversible encoding (portable, no key to
carry — convenience, not a security boundary); set `MailCal:TokenEncryptionKey` to AES-encrypt
them at rest (stays portable as long as the same key is present on the target).

```sh
# One-time bootstrap for the pure-service case (browser or device-code)
MailCalMCPSharp --auth work
MailCalMCPSharp --auth personal --auth-mode devicecode
```

## Run

### Standalone

```sh
MailCalMCPSharp
# HTTP MCP endpoint on http://localhost:5708/mcp
```

### Docker

```sh
docker run --rm -p 5708:5708 \
  -e MAILCALMCP_Server__Password=change-me \
  -e MAILCALMCP_MailCal__ReadOnly=true \
  -v mailcal-tokens:/data/tokens \
  ghcr.io/wixely/mailcalmcpsharp:latest
```

### Windows Service

```bat
sc.exe create MailCalMCPSharp binPath= "C:\Services\MailCalMCPSharp\MailCalMCPSharp.exe"
```

## Configuration

Settings live in `MailCalMCPSharp.json` (plus `appsettings*.json` for compatibility) and can
be overridden by environment variables prefixed `MAILCALMCP_` using `__` for nesting.

| Setting | Purpose | Default |
| --- | --- | --- |
| `MailCal:ReadOnly` | Block all write/delete/authorize tools when true. | `true` |
| `MailCal:AllowPermanentDelete` | Second gate for hard delete (soft/trash otherwise). | `false` |
| `MailCal:DefaultAccount` | Alias used when a tool omits `account`. | first account |
| `MailCal:DefaultPageSize` | Page size for list operations. | `25` |
| `MailCal:MaxPages` | Max pages traversed for paged calls. | `4` |
| `MailCal:MaxBodyChars` | Body truncation limit (with a `truncated` flag). | `20000` |
| `MailCal:TokenStoreDirectory` | Portable token folder. | `tokens` |
| `MailCal:TokenEncryptionKey` | Blank = basic encoding; set (or `file:`) = AES at rest. | `""` |
| `MailCal:EnableMail` / `EnableCalendar` | Expose those tool groups. | `true` |
| `MailCal:EnableContacts` / `EnableRules` / `EnableScheduledSend` | v2 features. | `false` |
| `Server:Host` / `Port` / `Path` | HTTP bind + MCP route. | `localhost` / `5708` / `/mcp` |
| `Server:Password` | Optional MCP endpoint password. | `""` |
| `Server:WindowsServiceName` | SCM service name. | `MailCalMCPSharp` |

Each entry under `MailCal:Accounts` has an `Alias`, a `Provider` (`Outlook` or `Gmail`),
an `AuthType` (`Delegated`), and OAuth client credentials (`ClientId`, `ClientSecret`,
`TenantId` for Outlook). OAuth tokens are **not** stored here — they live in the token folder.

```
MAILCALMCP_MailCal__ReadOnly=false
MAILCALMCP_MailCal__Accounts__0__ClientId=<app-client-id>
MAILCALMCP_MailCal__TokenStoreDirectory=/data/tokens
MAILCALMCP_MailCal__TokenEncryptionKey=file:/run/secrets/token-key
MAILCALMCP_Server__Password=change-me
```

## Safety

- Read-only by default — every write/delete/authorize tool refuses until `MailCal:ReadOnly=false`.
- Hard delete needs the extra `MailCal:AllowPermanentDelete` switch; otherwise deletes go to trash.
- Only configured accounts are reachable; an unknown alias returns the list of valid aliases.
- Provider capability gating returns a clean "not supported by <provider>" message.
- Secrets and tokens are never logged or returned by any tool.

## Tools

| Group | Tools |
| --- | --- |
| Accounts / auth | `mailcal_list_accounts`, `mailcal_auth_status`, `mailcal_authorize`, `mailcal_deauthorize` |
| Email | `mail_list_folders`, `mail_read`, `mail_list`, `mail_search`, `mail_compose_draft`, `mail_send`, `mail_delete`, `mail_move` |
| Calendar | `cal_list_calendars`, `cal_read`, `cal_get_event`, `cal_search`, `cal_create_event`, `cal_update_event`, `cal_delete_event`, `cal_respond_event` |

## License

MIT — see [LICENSE](LICENSE). Third-party dependencies (all MIT or Apache-2.0) are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
