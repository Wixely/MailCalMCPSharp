# Setting up an Outlook account

MailCalMCPSharp talks to Outlook / Microsoft 365 through the Microsoft Graph API using
**delegated OAuth**. You register one application in Microsoft Entra ID (formerly Azure AD),
put its **Application (client) ID** in config, and then sign in once — no password or token is
ever stored in the config file.

**What you'll end up with:** an account entry with `Provider: "Outlook"`, a `ClientId`, and a
`TenantId`. `ClientSecret` stays **blank** (this uses a public client + PKCE).

---

## 1. Register an application in Microsoft Entra ID

1. Go to the [Azure Portal](https://portal.azure.com) → **Microsoft Entra ID** → **App
   registrations** → **New registration**.
2. **Name:** anything, e.g. `MailCalMCPSharp`.
3. **Supported account types:** pick based on the mailboxes you'll connect:
   - *Accounts in any organizational directory and personal Microsoft accounts* → use
     `TenantId: "common"` (works for both work/school and personal Outlook.com).
   - *Single tenant* → use your tenant's GUID or domain as `TenantId`.
4. **Redirect URI:** platform **Public client/native (mobile & desktop)**, value
   `http://localhost`. (You can also add this later in step 3.)
5. Click **Register**.

## 2. Copy the identifiers

On the app's **Overview** page, copy:
- **Application (client) ID** → this is your `ClientId`.
- **Directory (tenant) ID** → your `TenantId` (or keep `"common"` if you chose multi-tenant +
  personal accounts).

## 3. Configure authentication (public client)

1. Open the **Authentication** blade.
2. Under **Platform configurations**, ensure a **Mobile and desktop applications** platform
   exists with the redirect URI `http://localhost`. Add it if missing.
3. Scroll to **Advanced settings** → **Allow public client flows** → set to **Yes**.
   (Required for the interactive loopback flow and for device-code sign-in.)
4. **Save.**

> No client secret is required. Leave `ClientSecret` blank in config.

## 4. (Optional) Pre-declare API permissions

Consent is requested at sign-in time, so this is optional, but it documents intent and lets an
admin grant consent up front. Under **API permissions** → **Add a permission** → **Microsoft
Graph** → **Delegated permissions**, add:

| Scope | Used for |
| --- | --- |
| `Mail.ReadWrite` | read / draft / move / delete mail |
| `Mail.Send` | send mail |
| `Calendars.ReadWrite` | calendar read/write |
| `Contacts.ReadWrite` | contacts |
| `MailboxSettings.ReadWrite` | inbox rules |
| `offline_access` | refresh token (unattended running) |

For **work/school** tenants an administrator may need to **Grant admin consent** depending on
tenant policy. For **personal** accounts, you consent yourself at sign-in.

## 5. Configure the account

In `MailCalMCPSharp.json` (or via `MAILCALMCP_` env vars):

```jsonc
{
  "Alias": "work",
  "Provider": "Outlook",
  "AuthType": "Delegated",
  "TenantId": "common",          // or your tenant GUID/domain
  "ClientId": "<application-client-id>",
  "ClientSecret": ""             // leave blank for public client
}
```

## 6. Authorize (one time)

Either let the agent do it — call the `mailcal_authorize` tool with `account: "work"` — or run
the CLI:

```sh
MailCalMCPSharp --auth work
# no browser available (Docker/SSH)? use device code:
MailCalMCPSharp --auth work --auth-mode devicecode
```

A browser opens, you sign in (reusing your existing session if already logged in) and approve.
The refresh token is written to the exe-adjacent token folder (`MailCal:TokenStoreDirectory`).
The server then runs unattended.

---

## Troubleshooting

- **`AADSTS7000218` / "public client flows" error:** you missed step 3.3 — set *Allow public
  client flows* to **Yes**.
- **Redirect URI mismatch:** ensure `http://localhost` is registered under *Mobile and desktop
  applications* (step 3.2).
- **Admin consent required:** your tenant blocks user consent for these scopes — ask an admin to
  *Grant admin consent* on the app (step 4).
- **Wrong `TenantId`:** personal Outlook.com accounts need `"common"` (or `"consumers"`); a
  single-tenant GUID will reject them.
- **Re-authorize:** `mailcal_deauthorize` (or delete the account's file in the token folder),
  then authorize again.
