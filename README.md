# AnkiLearner

A self-hostable web app for learning a foreign language with spaced repetition (Anki-style),
plus AI-assisted word lookup. Generic by design — the learning language and your known languages
are per-user settings, so anyone can host an instance to learn any language.

The app's PostgreSQL database is the single source of truth; Anki is only an import/export format.

## Status

**v1 feature-complete** (spec phases 0–8): auth, dictionary, AI lookup, SM-2 study, and
`.apkg` import are implemented and tested. Production packaging/CI/CD (phase 9) is deferred.
This repo is the production rewrite of an earlier proof-of-concept
(`C:\Projects\DanishLearner`, Razor Pages + SQLite).

## Stack

- **Backend:** ASP.NET Core 10 Web API, EF Core, ASP.NET Core Identity + JWT
- **Frontend:** Angular (latest stable) + Angular Material
- **Database:** PostgreSQL
- **AI lookup:** Claude Haiku via the `Anthropic` SDK (behind a provider interface)
- **Packaging:** Docker / docker-compose (CI/CD and production hosting deferred)

## v1 features

Multi-user accounts · personal dictionary (CRUD, tags, search) · add words manually or via AI
lookup (meanings, IPA, part of speech, gender, example) · AnkiDroid `.apkg` import including
study progress · SM-2 spaced repetition in both directions with inline correction · responsive
UI (study from a phone browser).

## Running locally (development)

```bash
docker compose up -d db                        # Postgres 16 on localhost:5433
dotnet run --project backend/AnkiLearner.Api   # API on http://localhost:5080 (migrates on start)
cd frontend && npm install && npm start        # SPA on http://localhost:4200, proxies /api
```

## Configuration

| Key | Required | Default | Purpose |
|---|---|---|---|
| `ConnectionStrings:Default` | yes | dev: local compose db | PostgreSQL connection string |
| `Jwt:SigningKey` | yes | dev-only key in `appsettings.Development.json` | HMAC key for access tokens, ≥ 32 chars — generate a unique one per instance |
| `Jwt:Issuer` / `Jwt:Audience` | no | `AnkiLearner` | JWT validation values |
| `Jwt:AccessTokenMinutes` / `Jwt:RefreshTokenDays` | no | `15` / `30` | Token lifetimes |
| `Auth:AllowRegistration` | no | `true` | Set `false` to close self-signup (single-user instance) |
| `RateLimiting:AuthPerMinute` | no | `20` | Per-IP limit on credential endpoints |
| `RateLimiting:LookupPerMinute` | no | `20` | Per-user limit on AI lookups |
| `Anthropic:ApiKey` | no | *(empty — AI lookup disabled)* | Server-level Claude API key; the bare `ANTHROPIC_API_KEY` env var also works |
| `Anthropic:Model` | no | `claude-haiku-4-5` | Model used for word lookup |
| `Import:MaxUploadBytes` | no | `52428800` (50 MB) | Size cap for uploaded `.apkg` files |

All keys can be provided as environment variables (`Jwt__SigningKey=...`) or via
`dotnet user-secrets` in development. The API refuses to start without a valid signing key.

## Documentation

- [`CLAUDE.md`](CLAUDE.md) — project context and locked decisions (for Claude Code agents)
- [`docs/SPECIFICATION.md`](docs/SPECIFICATION.md) — detailed functional + technical spec
- [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md) — phased build plan for implementers
