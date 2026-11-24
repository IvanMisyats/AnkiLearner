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
| 8 | SRS | **FSRS-6** since 2026-11-24 (ADR-0013; py-fsrs port, per-user retention + weights, max interval). v1 shipped SM-2 |
| 9 | Backend | **.NET 10 Web API** (controllers), EF Core, layered projects |
| 10 | Frontend | **Angular** (latest stable, standalone components + signals) + Angular Material. Re-confirmed 2026-07-02 over Blazor — but see the frontend-clarity convention below |
| 11 | Database | **PostgreSQL** |
| 12 | Packaging | **Docker** (multi-container via compose) |
| 13 | CI/CD + reverse proxy | Deferred in v1; since done: GitHub Actions → GHCR → VPS, host nginx + Cloudflare (ADR-0002) |
| 14 | Anki progress | **Import scheduling state** from `.apkg` (map to SM-2 fields, then FSRS setup) **+ review history** (`revlog` → review log; "history only" re-upload) |
| 15 | Study model | **Combined directions**: `TargetToKnown` shows all known-language translations together; per-language directions deferred |
| 16 | Auth scope | **Minimal v1**: register/login/logout + JWT/refresh; no email verification, no password-reset email |
| 17 | Script access | **Personal access tokens** (`ankl_` prefix) as a second auth scheme; full-account scope, managed only by a password login. No scopes, no management UI. |

## Tech stack

- **Backend:** ASP.NET Core 10 Web API. Layered: `Api` / `Core` / `Infrastructure` (+ `Tests`).
  ASP.NET Core Identity for the user store, **JWT** access tokens + refresh tokens for the SPA.
- **ORM / DB:** EF Core 10 + **PostgreSQL** (`Npgsql.EntityFrameworkCore.PostgreSQL`).
- **AI lookup:** `Anthropic` NuGet SDK, model **`claude-haiku-5-5`** at effort `low` (both config:
  `Anthropic:Model` / `Anthropic:Effort`, `ANTHROPIC_MODEL` / `ANTHROPIC_EFFORT` in prod), structured outputs (JSON schema),
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
    ADR.md                    # decision/feature records (why + what + cost), append-only
    BACKLOG.md                # known defects + follow-ups deliberately not fixed yet
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
- **SrsState**: FSRS memory (Stability, Difficulty) + schedule per `(WordId, Exercise)`; exercises
  `TargetToKnown`, `KnownToTarget` (typing/anagram later). Studied directions share the word but
  track progress independently. `EaseFactor`/`Repetitions` are legacy SM-2 inputs for FSRS setup.
- **ReviewLogEntry** (table `ReviewLog`): append-only history, one row per grade / relearn / reset
  per direction, with the state before and after. `SrsState` is only the latest snapshot.

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
- **Record every significant decision or feature** as a new entry in `docs/ADR.md` (append-only;
  supersede, don't rewrite). **Log defects found but not fixed** in `docs/BACKLOG.md`, and remove the
  item in the commit that fixes it. The repo is public: never log unfixed security vulnerabilities.

## Implementation status

**Phases 0–8 of `docs/IMPLEMENTATION_PLAN.md` are complete** (2026-07-02): backend
(auth/dictionary/lookup/SRS/import) with 54 integration+unit tests, Angular UI
(auth/dictionary/study/import/settings) with unit tests. Verified against the real
AnkiDroid export (947 notes → 944 words + 1424 SRS states). Phase 9 (Docker images,
CI/CD, reverse proxy) is done and deployed to `anki.misyats.com`.

**Grade-aware intervals** (2026-09-06, superseded by FSRS below): textbook SM-2 gave Hard/Good/Easy the same next
interval (they only diverged a review later, via ease), so all three button hints read alike.
`Sm2.Apply` now keeps the SM-2 ladder as the Good baseline and layers on Anki's hard factor
(1.2), easy bonus (1.3) and easy interval (4 d), capped by `UserSettings.MaxIntervalDays`
(default 36500, editable in Settings). See spec §5.

**Reset progress + reviewed-today counter** (2026-09-06): `DELETE /api/words/{id}/progress`
drops a word's SRS states in every direction (button in the word edit form, behind the new
`ConfirmService` Material dialog in `core/confirm.service.ts`). `/api/study/counts` gained
`reviewedToday`, shown as a third dashboard counter — derived from `SrsState.LastReviewed`
against the UTC day, so repeat presses on one word count once and a reset removes it from the
total. No schema change; see spec FR-D8 / FR-R1.

**API tokens** (2026-07-28): `[Authorize]` endpoints accept either a SPA JWT or a long-lived
`ankl_…` personal access token, routed by a policy scheme in `Program.cs`. Added so external
clients — starting with the `danish-word` Claude skill in `~/.claude/skills/` — can add words
without a browser session. See the API tokens section of `README.md`.

**Translate mode** (2026-11-16, merged and deployed): `/` is a
mode selector (Translate / Learn); `/translate` translates a word/phrase/sentence between the learning
and known languages via `POST /api/translate` (same Claude provider as lookup) and suggests
citation-form word cards ("en aftale") with one-tap add, tagged `translated`. Also: article-insensitive
duplicate matching (`TermNormalizer.MatchCandidates`), new cards served newest-first, "Study again"
(`POST /api/words/{id}/relearn`), the dashboard moved to `/study`, a safe login `returnUrl`, and an
installable Android PWA with a share target. Design record and deviations: **`docs/TRANSLATE_PLAN.md`**.

**AI model as config** (2026-11-16): lookup and Translate run on `claude-haiku-5-5` at effort
`low` (ADR-0010). Key, model and effort are `ANTHROPIC_*` lines in the VPS's `app.env` — the one
production settings file — and `anki-deploy` applies `app.env` edits on its own (compose
config-hash drift check, ADR-0011), so a new model needs no code change. See Operations in
`infra/README.md`.

**Review log** (2026-11-24): every grade, Study again and reset appends a row to `ReviewLog`
(FR-R11, ADR-0012): the history FSRS is fitted to and replayed over.

**FSRS-6 scheduler** (2026-11-24): replaces SM-2 (spec §5, ADR-0013). Engine `Core/Srs/Fsrs.cs`
(py-fsrs 6.3.3 port, calendar days like Anki, tests pinned to Anki's own memory states);
pre-FSRS cards are set up from Anki history / the SM-2 snapshot and rescheduled
(`FsrsInitializer`, after startup and on import / settings change); `.apkg` import can bring the
`revlog` ("history only" for an existing dictionary); new words come before a backlog, due cards
highest-recall first. Settings: desired retention + pasted weights (fit them with Anki's
optimizer, also headless via the `anki` pip package). Plan / design record: `docs/FSRS_PLAN.md`.

## How to run

```bash
# local Postgres for dev
docker compose up -d db

# backend (from backend/)
dotnet run --project AnkiLearner.Api          # EF migrations applied on startup

# frontend (from frontend/)
npm install && npm start                      # ng serve, proxies /api to the backend
```

**Working in a git worktree:** start the dev DB from the **main checkout**, not from the worktree.
Compose takes the project name from the folder, but `container_name: ankilearner-db-dev` is fixed.
Started from the worktree, compose would try to make a second container with the same name and
an empty volume. The worktree's API uses the main checkout's DB on `localhost:5433`, which is safe
unless the branch changes the schema.

### Tests

```bash
# backend (from backend/) — integration tests use Testcontainers, so Docker MUST be running
dotnet test

# frontend (from frontend/) — ng test defaults to WATCH mode; --watch=false for one-shot
npx ng test --watch=false
```

Server-level AI key (optional; AI lookup degrades gracefully without it):
`dotnet user-secrets set "Anthropic:ApiKey" (Get-Clipboard) --project backend/AnkiLearner.Api`
(or env `ANTHROPIC_API_KEY`). Integration tests swap in a fake provider, so a local key never
makes them call the API.
