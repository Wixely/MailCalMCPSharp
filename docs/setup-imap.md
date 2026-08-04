# Setting up a generic IMAP/SMTP account

The `Imap` provider connects to **any** mailbox that speaks IMAP (reading) and SMTP (sending),
using a plain **username and password** — there is **no OAuth** and nothing to register. It is
**email only** (no calendar, contacts, rules, or scheduled send).

**What you'll end up with:** an account entry with `Provider: "Imap"` and an `Imap` block holding
the host/port/username/password.

---

## 1. Get your provider's IMAP + SMTP settings

You need four values plus credentials. Typical settings:

| Setting | Typical value |
| --- | --- |
| IMAP host / port | `imap.<provider>` / `993` (implicit SSL) |
| SMTP host / port | `smtp.<provider>` / `587` (STARTTLS) or `465` (implicit SSL) |
| Username | usually your full email address |
| Password | your password, or an **app password** (see step 2) |

## 2. Use an app password if the provider requires one

Most providers with 2-factor authentication **won't accept your normal password** over IMAP/SMTP —
you must generate an app-specific password:

| Provider | IMAP | SMTP | Password |
| --- | --- | --- | --- |
| Gmail | `imap.gmail.com:993` | `smtp.gmail.com:587` | App password (needs 2-Step Verification on) |
| Outlook.com (personal) | `outlook.office365.com:993` | `smtp-mail.outlook.com:587` | App password |
| Fastmail | `imap.fastmail.com:993` | `smtp.fastmail.com:465` | App password |
| Yahoo | `imap.mail.yahoo.com:993` | `smtp.mail.yahoo.com:465` | App password |
| iCloud | `imap.mail.me.com:993` | `smtp.mail.me.com:587` | App password |

> **Microsoft 365 / work accounts:** Microsoft has **disabled basic IMAP/SMTP auth** for most
> Exchange Online tenants. For those mailboxes use the **Outlook (OAuth)** provider instead — see
> [setup-outlook.md](setup-outlook.md). This generic IMAP provider is best for personal/other IMAP
> hosts. For **Gmail**, prefer the OAuth **Gmail** provider unless you specifically want IMAP.

## 3. Configure the account

In `MailCalMCPSharp.json` (or via `MAILCALMCP_` env vars):

```jsonc
{
  "Alias": "imap",
  "Provider": "Imap",
  "Imap": {
    "ImapHost": "imap.example.com",
    "ImapPort": 993,
    "SmtpHost": "smtp.example.com",
    "SmtpPort": 587,
    "Username": "me@example.com",
    "Password": "file:secrets/imap.pass",   // literal string also works; file: keeps it out of JSON
    "FromAddress": "",                        // defaults to Username when blank
    "DisplayName": "",                        // optional display name on sent mail
    "Security": "auto"                        // auto | ssl | starttls | none
  }
}
```

- **`Password`** supports a `file:` prefix so the secret can live outside the JSON (the `tokens/`
  and `secrets/`-style files are gitignored). It is never logged.
- **`Security`** is normally `auto` (MailKit negotiates TLS from the port). Override with `ssl`
  (implicit TLS, e.g. port 465/993), `starttls` (upgrade on 587/143), or `none` (plaintext — avoid).

## 4. "Authorize" (nothing to do)

IMAP/SMTP accounts have no OAuth step. `mailcal_auth_status` reports **Authorized** once the host,
username, and password are set (and **NotConfigured** if something's missing).
`mailcal_authorize` simply confirms this — there's no browser sign-in.

Test the connection by calling `mail_list_folders` (or `mail_list`) for the account — a bad host
or password surfaces here as a connection/authentication error.

---

## Capabilities

IMAP/SMTP is **email only**. `mail_*` tools work (folders, read, list, search, draft, send,
delete, move). `cal_*`, `contact_*`, rule, and scheduled-send tools return a clear
*"IMAP/SMTP account supports email only"* message.

## Troubleshooting

- **Authentication failed:** you likely need an **app password** (step 2), not your normal
  password; or the account has IMAP disabled in its settings.
- **Basic auth disabled (M365/work):** use the Outlook OAuth provider instead.
- **TLS/connection errors:** try setting `Security` explicitly (`ssl` for 993/465, `starttls` for
  587/143) instead of `auto`.
- **Sends rejected / wrong From:** set `FromAddress` to the account's real address (some SMTP
  servers require From to match the authenticated user).
