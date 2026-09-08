# Notesnook Sync Server — SQLite fork

A fork of the [Notesnook Sync Server](https://github.com/streetwriters/notesnook-sync-server)
(upstream tag `v1.0-beta.36`) that **replaces MongoDB with SQLite** and is tuned to
run on a small, low-RAM VPS.

The upstream server needs MongoDB, MinIO/S3, Redis and a handful of Docker
containers — comfortably 700 MB+ of RAM before your notes. This fork runs the
**Identity server + sync API (+ optional SSE push)** as one process tree from a
single systemd unit, backed by one SQLite file, at **~150–250 MB** total. It is
meant for a personal instance: one user, a few devices.

## What changed

| Area | Upstream | This fork |
|---|---|---|
| Database | MongoDB (13 collections) | SQLite via EF Core 9 — one file, `sync_items` table with a `type` column |
| Identity / grants | `AspNetCore.Identity.Mongo`, custom Mongo grant store | EF Core Identity + `IdentityServer4.EntityFramework` operational store, same SQLite file |
| Redis | TTL / SignalR backplane / cache | removed — single instance, IdentityServer4 + Quartz handle expiry, SQLite handles the rest |
| Object storage | MinIO container | any S3-compatible endpoint (e.g. Cloudflare R2), configured in `.env` |
| Monograph public pages | separate `monograph-server` (Bun/TS) | rendered in-process by the API at `GET /share/{key}` (pure-JS `@noble` crypto for the unlock page, no eval/WASM) |
| Packaging | Docker Compose | bare metal: one systemd unit + a supervisor script, no containers |
| Web clipper (`Notesnook.Inbox.API`) | standalone Bun/TS service | in-app: inbox key management + `/inbox/*` on the main API |

Not carried over: the Docker/Compose setup, the standalone `monograph-server` /
`cors-proxy` / `themes` services, the external-backend Compose examples.

The wire protocol is unchanged — official Notesnook clients (desktop, mobile,
web) point at it with **Settings → Servers**.

## Build

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
dotnet restore Notesnook.sln
dotnet build   Notesnook.sln -c Release

# publish the three services
dotnet publish Notesnook.API/Notesnook.API.csproj             -c Release -o publish/api
dotnet publish Streetwriters.Identity/Streetwriters.Identity.csproj -c Release -o publish/identity
dotnet publish Streetwriters.Messenger/Streetwriters.Messenger.csproj -c Release -o publish/sse
```

## Deploy

See [`example/`](example/): a systemd unit, the supervisor script, an annotated
`.env`, reverse-proxy configs for **Caddy, nginx and Apache**, and a full
step-by-step in [`example/SETUP.md`](example/SETUP.md).

`.github/workflows/deploy.yml` is a working GitHub Actions pipeline that builds
the three services and ships them to a host over SSH — adapt the final step to
your setup (it just needs a way to drop the built tree on the box and restart
the unit).

## License

AGPLv3, unchanged from upstream. Original work © Streetwriters (Private) Limited;
see `AUTHORS` and the header in every source file.

```
This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
Affero GNU General Public License for more details.

You should have received a copy of the Affero GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
```
