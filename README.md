# AnkiLearner

A self-hostable web app for learning a foreign language with spaced repetition (Anki-style),
plus AI-assisted word lookup. Generic by design — the learning language and your known languages
are per-user settings, so anyone can host an instance to learn any language.

The app's PostgreSQL database is the single source of truth; Anki is only an import/export format.

## Status

**v1 feature-complete** (spec phases 0–8): auth, dictionary, AI lookup, FSRS study, and
`.apkg` import are implemented and tested. Production packaging/CI/CD (phase 9) is deferred.
This repo is the production rewrite of an earlier proof-of-concept
(`C:\Projects\DanishLearner`, Razor Pages + SQLite).

## Stack

- **Backend:** ASP.NET Core 10 Web API, EF Core, ASP.NET Core Identity + JWT
- **Frontend:** Angular (latest stable) + Angular Material
- **Database:** PostgreSQL
- **AI lookup + translate:** Claude Haiku via the `Anthropic` SDK (behind a provider interface)
- **Packaging:** Docker / docker-compose (CI/CD and production hosting deferred)

## v1 features

Multi-user accounts · personal dictionary (CRUD, tags, search) · add words manually or via AI
lookup (meanings, IPA, part of speech, gender, example) · AnkiDroid `.apkg` import including
study progress and review history · FSRS-6 spaced repetition (per-user retention and fitted weights) in both directions with inline correction · responsive
UI (study from a phone browser) · Translate mode: translate a word, phrase or sentence and add
the AI-picked words to your dictionary in one tap · installable Android app (PWA) with Translate /
Learn shortcuts that accepts text shared from other apps.

## Running locally (development)

```bash
docker compose up -d db                        # Postgres 16 on localhost:5433
dotnet run --project backend/AnkiLearner.Api   # API on http://localhost:5080 (migrates on start)
cd frontend && npm install && npm start        # SPA on http://localhost:4200, proxies /api
```

AI lookup needs a Claude API key (platform.claude.com → Settings → API keys). Locally, keep it in
.NET user-secrets — stored outside the repo, loaded only in Development:

```powershell
dotnet user-secrets set "Anthropic:ApiKey" (Get-Clipboard) --project backend/AnkiLearner.Api
```

In production it is `ANTHROPIC_API_KEY` in `app.env`, alongside `ANTHROPIC_MODEL` /
`ANTHROPIC_EFFORT` — the one file for production settings; edits apply within 5 minutes
(see `infra/app.env.example` and the Operations section of `infra/README.md`).

## Configuration

| Key | Required | Default | Purpose |
|---|---|---|---|
| `ConnectionStrings:Default` | yes | dev: local compose db | PostgreSQL connection string |
| `Jwt:SigningKey` | yes | dev-only key in `appsettings.Development.json` | HMAC key for access tokens, ≥ 32 chars — generate a unique one per instance |
| `Jwt:Issuer` / `Jwt:Audience` | no | `AnkiLearner` | JWT validation values |
| `Jwt:AccessTokenMinutes` / `Jwt:RefreshTokenDays` | no | `15` / `30` | Token lifetimes |
| `Auth:AllowRegistration` | no | `true` | Set `false` to close self-signup (single-user instance) |
| `RateLimiting:AuthPerMinute` | no | `20` | Per-IP limit on credential endpoints |
| `RateLimiting:LookupPerMinute` | no | `20` | Per-user limit on AI calls; word lookup and translate share it |
| `Anthropic:ApiKey` | no | *(empty — AI lookup and translate disabled)* | Server-level Claude API key; the bare `ANTHROPIC_API_KEY` env var also works |
| `Anthropic:Model` | no | `claude-haiku-5-5` | Model used for word lookup and translate — any Claude model id |
| `Anthropic:Effort` | no | `low` | Thinking depth (`low`…`max`); `default` sends none — needed for models without effort support (e.g. `claude-haiku-4-5`) |
| `Anthropic:MaxTokens` | no | `4096` | Output cap per AI call, thinking included — only a ceiling; a truncated answer is a 502 |
| `Anthropic:TimeoutSeconds` | no | `30` | Total time limit per AI call, SDK retries included; exceeded ⇒ 502 |
| `Import:MaxUploadBytes` | no | `52428800` (50 MB) | Size cap for uploaded `.apkg` files |

All keys can be provided as environment variables (`Jwt__SigningKey=...`) or via
`dotnet user-secrets` in development. The API refuses to start without a valid signing key.

## API tokens

Non-interactive clients (scripts, CLI tools) can't hold the browser's httpOnly refresh cookie, so
they authenticate with a long-lived personal access token instead of a JWT. A token carries the
**same rights as your account** — treat it like a password and revoke it if it leaks.

Mint one from an ordinary password login (a token cannot mint another token):

```bash
BASE=https://anki.misyats.com
JWT=$(curl -s -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' \
  -d '{"email":"you@example.com","password":"..."}' | jq -r .accessToken)

curl -s -X POST "$BASE/api/tokens" -H "Authorization: Bearer $JWT" \
  -H 'Content-Type: application/json' -d '{"name":"my-script"}' | jq -r .value
```

The raw value (`ankl_…`) is returned **once** — only its SHA-256 hash is stored. Send it as a
bearer token to any endpoint the SPA uses:

```bash
curl -s "$BASE/api/auth/me" -H "Authorization: Bearer ankl_..."
```

| Verb | Route | Purpose |
|---|---|---|
| `POST` | `/api/tokens` | `{ "name": "...", "expiresInDays": 90 }` — omit `expiresInDays` to never expire |
| `GET` | `/api/tokens` | list active tokens (metadata only; never the value) |
| `DELETE` | `/api/tokens/{id}` | revoke immediately |

All three require a password-backed JWT, so a leaked token cannot extend or hide itself.

## Documentation

- [`CLAUDE.md`](CLAUDE.md) — project context and locked decisions (for Claude Code agents)
- [`docs/SPECIFICATION.md`](docs/SPECIFICATION.md) — detailed functional + technical spec
- [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md) — phased build plan for implementers
- [`docs/decisions/`](docs/decisions/) — why it is built this way: the decision log (ADRs) and design records
