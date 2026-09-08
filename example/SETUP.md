# Bare-metal install

**One systemd unit** (`notesnook.service`) runs a supervisor (`run.sh`) that
launches the Identity server + sync API (+ optional SSE) as children in one
cgroup. SQLite backend, one file: `/opt/notesnook/data/notesnook.db`.

There is no server GUI. You administer it with `systemctl`, the `.env` file, and
occasionally `sqlite3`. The client is the Notesnook app (desktop / mobile / web)
pointed at your server URLs.

```
notesnook.example.com        ->  127.0.0.1:5264   sync API + SignalR hub  (+ optional web client)
auth.notesnook.example.com   ->  127.0.0.1:8264   Identity / OAuth
events.notesnook.example.com ->  127.0.0.1:7264   SSE push   (required by the client)
```

Paths below use `/opt/notesnook` and a system user `notesnook`; adjust to taste.
`5264 / 8264 / 7264` are Notesnook's standard internal ports (localhost only,
behind the reverse proxy) — change them in `.env` and the proxy config together
if they clash with something.

---

## 1. .NET runtime

Install the **ASP.NET Core 9 runtime** (`aspnetcore-runtime-9.0`) from your
distro or from https://dotnet.microsoft.com/download/dotnet/9.0.

## 2. System user

```bash
sudo useradd --system --home-dir /opt/notesnook --no-create-home \
     --shell /usr/sbin/nologin --comment 'Notesnook sync server' notesnook
```

## 3. Lay down the app

Build the three services (see the repo README) and assemble this tree:

```
/opt/notesnook/
  run.sh                 # example/run.sh
  .env                   # from example/env.example
  api/                   # dotnet publish of Notesnook.API
  identity/              # dotnet publish of Streetwriters.Identity
  sse/                   # dotnet publish of Streetwriters.Messenger
```

```bash
sudo mkdir -p /opt/notesnook/{data,logs,identity/keystore}
sudo cp example/run.sh /opt/notesnook/run.sh
sudo cp example/env.example /opt/notesnook/.env
# ...copy the three published dirs into api/ identity/ sse/...

sudo chown -R notesnook:notesnook /opt/notesnook
sudo chmod 750 /opt/notesnook/run.sh
sudo chmod 600 /opt/notesnook/.env
sudo chmod 700 /opt/notesnook/data /opt/notesnook/identity/keystore
```

## 4. Configure `.env`

Open `/opt/notesnook/.env` and set, at minimum:

- `NOTESNOOK_API_SECRET` — `openssl rand -hex 32`
- the three public `*_URL` / `*_HOST` values to your domains
- `S3_*` — any S3-compatible bucket for attachments (Cloudflare R2, MinIO,
  Backblaze B2, AWS S3…)
- `SMTP_*` — optional. With `SELF_HOSTED=1` signup auto-confirms the email, and
  authenticator-app 2FA needs no mail, so you can start without it. If SMTP is
  unset, the first-login email-2FA code is written to the journal instead.

Leave `DISABLE_SIGNUPS=false` until the one account exists (step 7).

## 5. Install the unit

```bash
sudo cp -r example /opt/notesnook/example      # so Documentation= resolves
sudo install -m 0644 example/notesnook.service /etc/systemd/system/notesnook.service
sudo systemctl daemon-reload
sudo systemctl enable --now notesnook.service
```

First start runs the EF migrations and creates `data/notesnook.db`:

```bash
journalctl -u notesnook -f
curl -sS localhost:8264/health && echo    # identity
curl -sS localhost:5264/health && echo    # api
```

## 6. Reverse proxy

Pick one — configs in this directory:

- **Caddy**  — `example/caddy.conf`  (automatic TLS)
- **nginx**  — `example/nginx.conf`
- **Apache** — `example/apache.conf`  (needs `proxy_wstunnel` for the sync hub)

All three do the same thing: route the API's fixed route-prefixes and the
`/hubs/sync/v2` WebSocket to `:5264`, send everything else on the main host to
the optional web client, proxy `auth.` to `:8264`, and proxy `events.` to
`:7264` **without buffering or compression** (SSE breaks otherwise).

Then reach the health endpoints through TLS:

```bash
curl -sS https://auth.notesnook.example.com/health && echo
curl -sS https://notesnook.example.com/health && echo
```

## 7. Create the account, then lock signups

1. In the Notesnook client: **Settings → Servers** — set
   - Auth server:   `https://auth.notesnook.example.com`
   - Sync server:   `https://notesnook.example.com`
   - Events server: `https://events.notesnook.example.com`   (required)
   - Monograph server: `https://notesnook.example.com`
2. Sign up with your email + password. The email is auto-confirmed (`SELF_HOSTED=1`).
3. At the 2FA prompt (method = email): if SMTP is unset, read the code from
   `sudo journalctl -u notesnook --since '2 min ago' | grep -i '2FA'`, enter it,
   then switch 2FA to **Authenticator app** in the client.
4. Lock it down:

```bash
sudo sed -i 's/^DISABLE_SIGNUPS=.*/DISABLE_SIGNUPS=true/' /opt/notesnook/.env
sudo systemctl restart notesnook
```

## 8. Move existing notes

Signed into the new server in the same client, it re-uploads your notes and
attachments on the first sync. Or use the client's export → import.

---

## Operating it

| task | command |
|---|---|
| status / logs | `systemctl status notesnook` · `journalctl -u notesnook -f` |
| restart | `sudo systemctl restart notesnook` |
| DB shell | `sudo -u notesnook sqlite3 /opt/notesnook/data/notesnook.db` |
| backup | copy `data/notesnook.db*` (use `sqlite3 .backup` for a hot copy) **and** `identity/keystore/` — losing the keystore invalidates every issued token/cookie |
| update | replace `api/ identity/ sse/` (keep `identity/keystore/`), `chown -R notesnook:notesnook`, `systemctl restart notesnook`; migrations auto-apply on boot |

## Footprint

Identity + API idle ~90–150 MB, ~200–250 MB under load, for the whole cgroup
(`systemctl status notesnook` → Memory). Add ~60–80 MB for SSE. `MemoryMax` in
the unit leaves first-run migration headroom.

## Not included (optional upstream projects)

- **`monograph-server`** — not needed; public monograph pages render in the API
  (`GET /share/{key}`): plain notes sanitized under a no-script CSP,
  password-protected notes get an in-browser Argon2i + XChaCha20-Poly1305 unlock
  page (pure-JS `@noble` crypto, no eval/WASM).
- **`Notesnook.Inbox.API`** (web clipper) — in-app instead: inbox key management
  + `/inbox/*` on the main API.
- **`notesnook-web`** — the web client SPA is a separate build; drop it in
  `/opt/notesnook/web` and the proxy configs serve it off the main host. Without
  it, use the desktop/mobile apps and set the main-host proxy to `return 404`
  for non-API paths.
- `cors-proxy`, `themes` server.
