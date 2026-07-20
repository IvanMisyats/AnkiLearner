# AnkiLearner — generic spaced-repetition language-learning site

> Context for Claude Code agents working on this repo. Keep this file current when
> decisions or architecture change. For full detail see **`docs/SPECIFICATION.md`**
> (what to build) and **`docs/IMPLEMENTATION_PLAN.md`** (how/when to build it, phased).

## What this is

A self-hostable web app for learning a foreign language with **spaced repetition** (Anki-style),
plus **AI-assisted word lookup** when adding words. The app's own **PostgreSQL database is the
single source of truth**; Anki is only an import/export target. This leaves room for custom
game-like exercises (word assembler, crossword) that Anki can't do.

The product is **generic**: the *learning language* and the user's *known languages* are settings,
so anyone can host an instance to learn any language. The primary author's case is **learning
Danish, knowing English + Ukrainian**.

## Prior art — the POC (read it, don't copy the stack)

There is a working proof-of-concept at **`C:\Projects\DanishLearner\`** (separate repo). It is an
ASP.NET Core **Razor Pages + EF Core + SQLite**, single-user, local app. AnkiLearner is the
**production rewrite** of that POC with a different stack (API + Angular + PostgreSQL, multi-user,
hosted). Reuse the POC's **domain logic and research**, not its delivery mechanism:

- `Services/Sm2.cs` — the **SM-2** engine (Again/Hard/Good/Easy → quality 2/3/4/5). Port this.
- `Services/WordLookupService.cs` — Claude structured-output word lookup. Port the prompt/schema,
  switch the model to **Claude Haiku** and put it behind a provider interface.
- `Models/Word.cs`, `Models/SrsState.cs` — the data model to generalize.
- `CLAUDE.md` in that repo — the **`.apkg` v3 format research** (zstd + SQLite + `unicase`
  collation, `0x1f` field separator). Essential for the v1 importer.
- The POC has **945 real words** imported from AnkiDroid — useful seed/test data.

## Confirmed decisions (locked)

| # | Decision | Choice |
|---|----------|--------|
| 1 | Users | **Multi-user accounts** (registration + login, per-user data isolation) |
| 2 | Language model | **1 target language + multiple known languages** per user (settings) |
| 3 | Anki import | **`.apkg` import in v1** (native C# parser; Anki decks → tags) |
| 4 | Add-word AI | **Rich AI lookup via Claude Haiku**, behind a provider interface |
| 5 | API key | **Server-level key** (operator-provided env/config); AI degrades gracefully if absent |
| 6 | Text format | **Sanitized HTML** for word/translation content (round-trips with Anki) |
| 7 | Organization | **Tags + study-all** (flat dictionary, free-form tags, filter studies by tag) |
| 8 | SRS | **SM-2** for v1 (FSRS is a future option) |
| 9 | Backend | **.NET 10 Web API** (controllers), EF Core, layered projects |
| 10 | Frontend | **Angular** (latest stable, standalone components + signals) + Angular Material. Re-confirmed 2026-07-02 over Blazor — but see the frontend-clarity convention below |
| 11 | Database | **PostgreSQL** |
| 12 | Packaging | **Docker** (multi-container via compose) |
| 13 | CI/CD + reverse proxy | **Deferred** — not part of v1. Choose Caddy/Traefik/nginx later. |
| 14 | Anki progress | **Import scheduling state** from `.apkg` (best-effort map of interval/ease/due → SM-2; optional at commit, default on) |
| 15 | Study model | **Combined directions**: `TargetToKnown` shows all known-language translations together; per-language directions deferred |
| 16 | Auth scope | **Minimal v1**: register/login/logout + JWT/refresh; no email verification, no password-reset email |

## Tech stack

- **Backend:** ASP.NET Core 10 Web API. Layered: `Api` / `Core` / `Infrastructure` (+ `Tests`).
  ASP.NET Core Identity for the user store, **JWT** access tokens + refresh tokens for the SPA.
- **ORM / DB:** EF Core 10 + **PostgreSQL** (`Npgsql.EntityFrameworkCore.PostgreSQL`).
- **AI lookup:** `Anthropic` NuGet SDK, model **`claude-haiku-4-5`**, structured outputs (JSON schema),
  behind `IWordLookupProvider`. Other providers (DeepL/Google) can be added later.
- **HTML safety:** sanitize on write (server, e.g. `Ganss.Xss`) and bind safely on render (Angular
  `DomSanitizer` / DOMPurify). Never render unsanitized user HTML.
- **Anki `.apkg`:** `ZstdSharp.Port` (decompress) + `Microsoft.Data.Sqlite` (read), custom
  `unicase` collation registered on the connection.
- **Frontend:** Angular standalone components, signals for state, `HttpClient` with a JWT
  interceptor, route guards, Angular Material. UI in English first, i18n-ready.
- **Container:** Dockerfiles for api + frontend; `docker-compose.yml` for local dev (Postgres +
  api + frontend). Production deploy/compose and TLS are a **later phase**.

## Repository layout (target)

```
AnkiLearner/                  # repo root
  CLAUDE.md                   # this file
  README.md
  docs/
    SPECIFICATION.md          # detailed functional + technical spec
    IMPLEMENTATION_PLAN.md    # phased plan for implementing agents
  backend/
    AnkiLearner.slnx          # solution (new XML format; dotnet sln commands work with it)
    AnkiLearner.Api/          # controllers, DI, auth, middleware, Program.cs
    AnkiLearner.Core/         # domain entities, enums, DTOs, SRS engine, provider interfaces
    AnkiLearner.Infrastructure/  # EF Core DbContext + migrations, Claude provider, Anki importer
    AnkiLearner.Tests/        # unit + integration tests
  frontend/                   # Angular workspace (app, features, services)
  docker-compose.yml          # local dev (Postgres + api + frontend)
```

## Core domain (summary — full schema in the spec)

- **User** (Identity) ── 1:1 ── **UserSettings** (`LearningLanguage`, `KnownLanguages[]`, limits).
- **Word** (owned by user): `LanguageCode` (the word's target language — switching the learning
  language hides, never deletes), target `Term` (HTML), `Transcription`, `PartOfSpeech`, `Notes`,
  `Example` (target lang), timestamps.
- **WordTranslation**: per known language `{ LanguageCode, Text (HTML), ExampleTranslation }`;
  unique per `(WordId, LanguageCode)`.
- **Tag** (per user) ── M:N ── **Word** (via `WordTag`).
- **SrsState**: SM-2 state per `(WordId, Exercise)`; exercises `TargetToKnown`, `KnownToTarget`
  (typing/anagram later). Studied directions share the word but track progress independently.

## Conventions

- **Prefer simple solutions over complex ones.** Don't over-engineer; no premature abstractions.
- **Frontend clarity:** the owner is a .NET developer with **no Angular/TypeScript experience**.
  Write plain, idiomatic Angular; no clever TS tricks; small components; add a short comment
  wherever an Angular/TS concept would surprise a C# developer. Explain frontend design choices
  in commit/PR descriptions.
- **Mobile-first study flow:** the primary study device is a phone browser; all screens responsive.
- All persisted user HTML is **sanitized server-side** before save.
- Every query that touches user data is **scoped by the authenticated `UserId`**.
- Times are stored/compared in **UTC**.
- Temporary files go in the scratchpad, **not** the repo.
- Be concise and direct in code comments and PRs.
- New env config keys are documented in `README.md` and have safe defaults / graceful degradation.

## Implementation status

**Phases 0–8 of `docs/IMPLEMENTATION_PLAN.md` are complete** (2026-07-02): backend
(auth/dictionary/lookup/SRS/import) with 54 integration+unit tests, Angular UI
(auth/dictionary/study/import/settings) with unit tests. Verified against the real
AnkiDroid export (947 notes → 944 words + 1424 SRS states). Phase 9 (Docker images,
CI/CD, reverse proxy) remains deferred — do not start it without an explicit go-ahead.

## How to run

```bash
# local Postgres for dev
docker compose up -d db

# backend (from backend/)
dotnet run --project AnkiLearner.Api          # EF migrations applied on startup

# frontend (from frontend/)
npm install && npm start                      # ng serve, proxies /api to the backend
```

### Tests

```bash
# backend (from backend/) — integration tests use Testcontainers, so Docker MUST be running
dotnet test

# frontend (from frontend/) — ng test defaults to WATCH mode; --watch=false for one-shot
npx ng test --watch=false
```

Server-level AI key (optional; AI lookup degrades gracefully without it):
`Anthropic:ApiKey` via user-secrets/env `ANTHROPIC_API_KEY`.
