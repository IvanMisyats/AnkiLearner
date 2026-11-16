# Production infrastructure

Everything needed to run AnkiLearner on the shared VPS. Host bootstrap (Docker, users, firewall,
TLS, DNS) is a host-level concern shared with the other application on the box and lives in the
private ops repo's `vps/bootstrap.md`; this guide starts from a bootstrapped host.

```
infra/
  docker-compose.yml          production stack (API + Postgres + db-setup)
  app.env.example             environment template
  db/scripts/01-app-user.sql  creates the non-superuser application role
  nginx/anki.misyats.com.conf vhost (pure proxy)
  deploy/                     anki-deploy.sh + user systemd units
  backup/                     restic scripts + user systemd units
```

## Architecture

```
GitHub Actions                     VPS
──────────────                     ───
CI: backend tests + frontend build
        │
CD: build image (Angular -> wwwroot, API)
    push ghcr.io/…:latest and :sha-abc1234
        │
        │ ssh (forced command, no shell)
        └──────────────────────────► /usr/local/bin/anki-deploy   (root-owned)
                                       docker compose pull / up -d
                                       wait for /api/health
                                               │
                        ┌──────────────────────┴─────────────────┐
                        │  rootless Docker daemon, user `anki`   │
                        │   ankilearner-api   127.0.0.1:8081     │
                        │   ankilearner-db    (no host port)     │
                        └────────────────────────────────────────┘
                                               ▲
                              host nginx ───────┘  (TLS: Cloudflare Origin CA)
```

Four properties are deliberate:

1. **The app runs as an unprivileged user with its own rootless Docker daemon.** `anki` has no
   sudo and is not in the `docker` group — that group is root-equivalent, and this box hosts
   QuestionsHub, whose data must stay unreachable from here.
2. **CI cannot run commands on the host.** The deploy key is pinned server-side to a forced
   command, so the only sentence it can utter is "redeploy".
3. **CI cannot change what runs.** The compose file is root-owned at `/srv/ankilearner/deploy/`.
4. **The SPA ships inside the API image.** Host nginx is a pure proxy: it needs no read access to
   this app's files, single-origin is preserved (no CORS, the httpOnly refresh cookie keeps
   working), and CI never writes anything onto the box.

## Layout on the host

| Path | Owner | Mode | Contents |
|---|---|---|---|
| `/srv/ankilearner/deploy/` | `root:anki` | 0750 | `docker-compose.yml`, `app.env`, `db/` |
| `/srv/ankilearner/backup/` | `anki:anki` | 0700 | `backup.env`, restic cache |
| `/usr/local/bin/anki-deploy` | `root:root` | 0755 | the forced command |
| `/usr/local/bin/anki-backup` | `root:root` | 0755 | backup entry point |

Postgres data is a named Docker volume (`ankilearner_pgdata`) inside the `anki` user's rootless
Docker root. There is no uploads directory — imported `.apkg` files are staged in memory between
preview and commit, so Postgres holds everything durable.

## Install

```bash
git clone https://github.com/IvanMisyats/AnkiLearner /tmp/anki && cd /tmp/anki

sudo install -o root -g anki -m 0640 infra/docker-compose.yml /srv/ankilearner/deploy/
sudo install -o root -g root -m 0755 -d /srv/ankilearner/deploy/db/scripts
sudo install -o root -g root -m 0644 infra/db/scripts/*.sql /srv/ankilearner/deploy/db/scripts/

sudo install -o root -g root -m 0755 infra/deploy/anki-deploy.sh      /usr/local/bin/anki-deploy
sudo install -o root -g root -m 0755 infra/backup/scripts/backup.sh   /usr/local/bin/anki-backup
sudo install -o root -g anki -m 0750 -d /srv/ankilearner/deploy/backup
sudo install -o root -g anki -m 0750 infra/backup/scripts/restore-*.sh /srv/ankilearner/deploy/backup/

sudo install -m 0644 infra/nginx/anki.misyats.com.conf /etc/nginx/conf.d/
sudo nginx -t && sudo systemctl reload nginx

sudo -u anki install -d -m 0755 /home/anki/.config/systemd/user
sudo -u anki install -m 0644 infra/deploy/systemd/* infra/backup/systemd/* /home/anki/.config/systemd/user/
sudo machinectl shell anki@ /bin/bash -c '
  systemctl --user daemon-reload &&
  systemctl --user enable --now anki-deploy.timer ankilearner-backup.timer ankilearner-restic-check.timer'
```

> `db/scripts/` is world-readable (0755/0644) on purpose: the Postgres container runs as its own
> UID, which maps into a subuid range rather than to `anki`, so it cannot read `root:anki 0640`
> files. The SQL is public; `app.env` and the compose file stay `0640`.

## Secrets

`/srv/ankilearner/deploy/app.env`, owned `root:anki` mode 0640 — readable by the app user,
writable only by root, so a stolen deploy key cannot rewrite the environment its own container
runs with. Template: `app.env.example`. Real values are in 1Password.

`Jwt:SigningKey` must be ≥32 characters and **unique to this instance**:
`openssl rand -base64 48`.

Set `ALLOW_REGISTRATION=true` only long enough to create the intended accounts, then set it back
to `false` and redeploy.

## GitHub secrets

| Secret | Value |
|---|---|
| `DEPLOY_SSH_KEY` | private half of the forced-command key |
| `DEPLOY_HOST` | VPS address |
| `DEPLOY_KNOWN_HOSTS` | output of `ssh-keyscan -p 55055 <vps-host>`, so CI verifies the host |

The image must be made **public** on GHCR once, after the first push — otherwise the VPS would
need a registry credential, and a token on the box able to push packages is exactly the
cross-application pivot this design removes.

## Backups

Own bucket, own S3 access key, own `RESTIC_PASSWORD`, host-tag `ankilearner` — all distinct from
QuestionsHub's. Each app's snapshot contains that app's `app.env`, so a shared repository would
leak one application's secrets to whoever holds the other's credentials.

Unlike QuestionsHub's, this restic repository is **new** and needs `restic init` once:

```bash
sudo machinectl shell anki@ /bin/bash -c '
  set -a; source /srv/ankilearner/backup/backup.env; set +a
  restic -r "s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic" init'
```

## Operations

All as the `anki` user (`sudo machinectl shell anki@`):

```bash
cd /srv/ankilearner/deploy
alias dc='docker compose --env-file app.env'

dc ps
dc logs -f api
/usr/local/bin/anki-deploy    # pull + converge (same thing CI triggers)
```

**Rollback** — every build publishes `:sha-<short>`:

```bash
sudo sed -i 's/^IMAGE_TAG=.*/IMAGE_TAG=sha-1a2b3c4/' /srv/ankilearner/deploy/app.env
sudo machinectl shell anki@ /usr/local/bin/anki-deploy
```

**Disaster recovery** — database first, API last (EF migrations would race `pg_restore --clean`):

```bash
docker compose --env-file app.env up -d postgres db-setup
./backup/restore-db-latest.sh
docker compose --env-file app.env up -d
```

## Smoke test

After a deploy, from any machine (through Cloudflare):

```bash
site=https://anki.misyats.com
show() { curl -sI "$site$1" | grep -iE '^(content-type|cache-control|cf-cache-status|age):'; }

show /translate               # text/html, no-cache  (a deep link gets index.html)
show /manifest.webmanifest    # application/manifest+json, no-cache
show /icons/icon-192.png      # image/png, no-cache
show /favicon.ico             # image/x-icon, no-cache
show /$(curl -s "$site/" | grep -oE 'main-[A-Za-z0-9]+\.js' | head -1)
                              # text/javascript, public, max-age=31536000, immutable
```

Only the `.js`/`.css` bundles may be `immutable` (Angular renames them on every change); every
other file keeps its name across deploys, so it must stay `no-cache` or browsers and the
Cloudflare edge keep serving the old one (`SpaStaticFiles.cs`). Bad results:

- A hashed bundle answered with `text/html`: the `UseRouting` order in `Program.cs` broke.
- An icon or the manifest answered with `text/html`: the file is missing from the image and the
  SPA fallback answered instead.
- A `no-cache` file with `cf-cache-status: HIT` and a large `age`: the edge serves a pinned old
  copy. Purge it (below).

## Android app (PWA)

The site installs as an app from Chrome on Android: `frontend/public/manifest.webmanifest`
(icons, Translate/Learn shortcuts, share target). There is no service worker. After a deploy
that touches the manifest or icons, check on the phone:

1. Chrome → open the site and sign in → ⋮ menu → **Install app** (older Chrome: **Add to home
   screen**). Chrome offers this only over HTTPS.
2. Optional, phone on USB with debugging on: desktop Chrome → `chrome://inspect` → inspect the
   tab → **Application → Manifest** shows no errors and renders the icons.
3. `chrome://webapks` on the phone lists AnkiLearner — it was installed as a real app (WebAPK),
   not as a bookmark shortcut.
4. Long-press the home-screen icon → **Translate** and **Learn** open `/translate` and `/study`.
5. Select text in another app (e.g. an article in Chrome) → **Share** → **AnkiLearner** → the
   Translate page opens and translates the selection. Only the *installed* app appears in the
   share sheet; it can take a minute or two after installing.

An installed app picks up manifest changes (name, icons, shortcuts) when Chrome next checks for
updates — at most daily, when the app is opened.

A share travels in the URL (GET), so one longer than about 8 KB once encoded (≈1,300 Cyrillic
or ≈7,000 Latin characters) is rejected by nginx/Kestrel with 414. Accepted: the page translates
at most 300 characters anyway, and such shares are rare.

**One-time, after the first deploy with the PWA:** purge Cloudflare's cache — Purge Everything,
or at least `/favicon.ico`, `/icons/*` and `/manifest.webmanifest`. Earlier builds served every
file except index.html as `immutable` for a year, so the edge may still hold the old favicon,
or the SPA's HTML fallback cached under an icon path requested before the icons existed.
(`index.html` links the favicon as `favicon.ico?v=2`, so browsers fetch the new one anyway.)

**CSP:** the live policy is only `frame-ancestors 'self'`, which doesn't restrict the manifest.
If a fuller CSP is added later, it must allow `manifest-src 'self'`.

### Icons

`frontend/public/icons/*.svg` are the sources; the PNGs beside them and
`frontend/public/favicon.ico` are rendered from them with a transparent background (they were
made with headless Chrome; any SVG renderer works, e.g. `inkscape icon.svg -w 192 -h 192 -o
icon-192.png`):

| Source | Outputs |
|---|---|
| `icon.svg` | `icon-192.png`, `icon-512.png`, `favicon.ico` (16, 32 and 48 px in one file) |
| `icon-maskable.svg` | `icon-maskable-512.png`, `apple-touch-icon.png` (180 px) |
| `shortcut-translate.svg`, `shortcut-learn.svg` | `shortcut-translate-96.png`, `shortcut-learn-96.png` |

Android crops the maskable icon to the launcher's shape (circle, squircle …), so its artwork
must stay inside the central circle of radius 40 % (205 px of 512). To build the `.ico` from
three PNG renders with Pillow:
`Image.open("f48.png").save("favicon.ico", sizes=[(16, 16), (32, 32), (48, 48)], append_images=[Image.open("f16.png"), Image.open("f32.png")])`.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `api` exits immediately | `read_only: true` — something tried to write outside `/tmp` |
| `permission denied` on the Docker socket | `DOCKER_HOST` unset in a non-login shell |
| Containers gone after reboot | `loginctl enable-linger anki` missing |
| Login works but sessions drop | `Jwt:SigningKey` changed between deploys |
| SPA loads, API calls 404 | `MapFallbackToFile` regex — unmatched `/api/*` must 404, not return HTML |
| `curl --resolve` to the origin fails TLS | Working as intended — Authenticated Origin Pulls |
| Deploy succeeds but nothing changed | `IMAGE_TAG` pinned to an old `sha-…` in `app.env` |
