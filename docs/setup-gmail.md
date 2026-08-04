# Setting up a Gmail account

MailCalMCPSharp talks to Gmail / Google Calendar / Google Contacts through the Google APIs using
**delegated OAuth**. You create an OAuth client in Google Cloud, put its **Client ID** and
**Client secret** in config, and sign in once — no password or token is stored in the config file.

**What you'll end up with:** an account entry with `Provider: "Gmail"`, a `ClientId`, and a
`ClientSecret`.

---

## 1. Create / select a Google Cloud project

1. Go to the [Google Cloud Console](https://console.cloud.google.com).
2. Create a new project (top bar → project selector → **New project**) or select an existing one.

## 2. Enable the APIs

**APIs & Services** → **Library**, then enable each of:
- **Gmail API**
- **Google Calendar API**
- **People API** (contacts)

## 3. Configure the OAuth consent screen

**APIs & Services** → **OAuth consent screen**:
1. **User type:**
   - **External** — for personal `@gmail.com` accounts.
   - **Internal** — only if this is a Google Workspace org and you're connecting org mailboxes.
2. Fill in app name, user support email, and developer contact.
3. **Scopes:** you may add the scopes the server uses (optional — they're requested at sign-in):
   `gmail.modify`, `gmail.send`, `gmail.settings.basic`, `calendar`, `contacts`.
4. **Test users:** while the app's publishing status is **Testing**, add the Google account(s)
   you'll connect as **Test users**. This lets you use the app without going through Google's
   verification review.

> **Important caveat (read this):** `gmail.modify` and `gmail.send` are **restricted** scopes.
> - In **Testing** status, refresh tokens **expire after 7 days** — you'd have to re-run
>   `mailcal_authorize` weekly. Fine for trying it out.
> - For durable unattended use you must move the app to **Production**, which for restricted
>   scopes requires **Google verification** (a security assessment). For personal/self use this is
>   the honest trade-off; plan for it.

## 4. Create an OAuth client ID

**APIs & Services** → **Credentials** → **Create credentials** → **OAuth client ID**:
- **Application type: Desktop app** (this is the right type for the loopback browser flow — Google
  auto-allows `http://localhost` loopback redirects for desktop clients).
- Name it, **Create**.
- Copy the **Client ID** and **Client secret**.

> **Device-code mode only:** the loopback (browser) flow uses a *Desktop app* client. If you need
> `--auth-mode devicecode` (no browser on the box), create a separate **"TVs and Limited Input
> devices"** OAuth client instead — Google's device flow requires that client type. Otherwise use
> browser mode and, for headless boxes, mint the token on a machine with a browser and copy the
> portable token folder over.

## 5. Configure the account

In `MailCalMCPSharp.json` (or via `MAILCALMCP_` env vars):

```jsonc
{
  "Alias": "personal",
  "Provider": "Gmail",
  "AuthType": "Delegated",
  "ClientId": "<client-id>.apps.googleusercontent.com",
  "ClientSecret": "<client-secret>"
}
```

`ClientSecret` supports a `file:` prefix (e.g. `"file:secrets/gmail.secret"`) to keep it out of
the JSON.

## 6. Authorize (one time)

Via the agent — call `mailcal_authorize` with `account: "personal"` — or the CLI:

```sh
MailCalMCPSharp --auth personal
```

A browser opens on a `http://localhost:<port>` loopback; sign in and approve. If the app is in
Testing status you'll see an **"Google hasn't verified this app"** screen — click **Advanced →
Go to \<app\> (unsafe)** to continue (expected for your own unverified app). The token is written
to the exe-adjacent token folder and the server runs unattended thereafter.

---

## Troubleshooting

- **"Google hasn't verified this app":** expected in Testing status — *Advanced → Go to app*.
- **Sign-in works, then stops after ~7 days:** Testing-status refresh-token expiry (see step 3
  caveat). Re-authorize, or publish to Production (with verification).
- **`invalid_client`:** wrong `ClientId`/`ClientSecret`, or the client type isn't *Desktop app*.
- **`access_blocked` / scope not allowed:** add your account under **Test users**, or the scope
  isn't enabled on the consent screen / API not enabled (step 2).
- **Device-code fails:** the client is a *Desktop app*, not *TVs and Limited Input devices* — see
  the device-code note in step 4.
- **Re-authorize:** `mailcal_deauthorize` (or delete the account's Google token files in the token
  folder), then authorize again.
