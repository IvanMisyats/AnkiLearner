# AnkiLearner

A self-hostable web app for learning a foreign language with spaced repetition (Anki-style),
plus AI-assisted word lookup. Generic by design — the learning language and your known languages
are per-user settings, so anyone can host an instance to learn any language.

The app's PostgreSQL database is the single source of truth; Anki is only an import/export format.

## Status

Greenfield. The design is complete; implementation has not started. This repo is the production
rewrite of an earlier proof-of-concept (`C:\Projects\DanishLearner`, Razor Pages + SQLite).

## Planned stack

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

## Documentation

- [`CLAUDE.md`](CLAUDE.md) — project context and locked decisions (for Claude Code agents)
- [`docs/SPECIFICATION.md`](docs/SPECIFICATION.md) — detailed functional + technical spec
- [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md) — phased build plan for implementers
