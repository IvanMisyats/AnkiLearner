# AnkiLearner — Implementation Plan (v1)

Phased, agent-friendly build plan. Each phase is independently shippable, lists **prerequisites**,
**tasks**, **deliverables**, and **acceptance criteria (AC)**. Hand a single phase to an agent at a
time. Authoritative scope lives in `SPECIFICATION.md`; locked decisions in `../CLAUDE.md`.

**Reference the POC** at `C:\Projects\DanishLearner\` for: `Services/Sm2.cs` (SM-2),
`Services/WordLookupService.cs` (Claude lookup), `Models/Word.cs` + `Models/SrsState.cs`, and the
`.apkg` v3 format notes in its `CLAUDE.md`.

## Conventions for implementing agents

- Stack & decisions are **locked** (see `../CLAUDE.md` §"Confirmed decisions"). Don't re-litigate.
- **Prefer simple solutions.** No speculative abstractions beyond what a phase needs.
- Every data query is **scoped by the authenticated `UserId`** (and, for words/study, by the
  current learning language). All user HTML is **sanitized on save**. Times in **UTC**.
- **Frontend clarity:** the owner is a .NET developer with no Angular/TypeScript experience.
  Plain idiomatic Angular, no clever TS, small components, comments where an Angular concept
  would surprise a C# dev. All screens **responsive**; study flow is phone-first.
- Each phase must **build, run, and pass its tests** before it's considered done. Add tests in the
  same phase as the code they cover.
- Conventional commits; keep PRs scoped to one phase. Update `../CLAUDE.md` and `README.md` when a
  phase changes how to run or configure the app.
- ~~**Do not** build CI/CD, production reverse proxy, or TLS — that is the deferred Phase 9.~~
  Phase 9 was built on 2026-07-25; see below and `infra/README.md`.
- Do not run `git push` (the user pushes manually). Commit freely on a feature branch.

---

## Phase 0 — Repository & toolchain scaffolding

**Prereqs:** none.

**Tasks**
1. Create solution `backend/AnkiLearner.sln` (.NET 10) with projects:
   `AnkiLearner.Api`, `AnkiLearner.Core`, `AnkiLearner.Infrastructure`, `AnkiLearner.Tests`.
   References: Api → Core, Infrastructure; Infrastructure → Core; Tests → all.
2. Create the Angular workspace under `frontend/` (latest stable, standalone, routing, SCSS,
   Angular Material). Add a dev proxy so `/api` → backend.
3. Add `docker-compose.yml` for **local dev**: a `db` service (PostgreSQL 16) with a named volume.
   (api/frontend compose services can be added now or in Phase 9 — local dev can run them on host.)
4. Extend `.gitignore` for Node/Angular (`node_modules/`, `dist/`, `.angular/`) and `.env`.
5. Add `backend/.editorconfig`, nullable + warnings-as-errors where reasonable, central package
   versions (`Directory.Packages.props`).
6. Add a `dotnet-tools.json` manifest with `dotnet-ef`.

**Deliverables:** compiling empty solution, runnable Angular shell, `docker compose up -d db` works.

**AC:** `dotnet build backend` succeeds; `npm install && npm run build` in `frontend` succeeds;
Postgres container starts and accepts connections.

---

## Phase 1 — Backend foundation: EF Core + Postgres + auth

**Prereqs:** Phase 0.

**Tasks**
1. `AnkiLearner.Infrastructure`: `AppDbContext` (Npgsql), wire `ConnectionStrings:Default`.
2. Add **ASP.NET Core Identity** (GUID keys) for the user store. `UserSettings` 1:1 entity with
   defaults; create settings row on registration.
3. Auth endpoints (§3.1, §6): register, login, refresh, logout, me. Issue **JWT access tokens**;
   **refresh token in httpOnly secure cookie** with rotation + server-side revocation list (simple
   table is fine). Config: `Jwt:*`, `Auth:AllowRegistration`.
4. Cross-cutting: `ICurrentUser` (resolves `UserId` from JWT), RFC-7807 error handling, CORS for the
   SPA origin, `GET /api/health`, structured logging, request validation.
5. Apply EF migrations on startup (dev) + an initial migration `InitialIdentity`.
6. Tests: integration tests for register→login→me→refresh→logout (Testcontainers Postgres).

**Deliverables:** working auth + user-scoped infrastructure.

**AC:** a new user can register and log in; protected endpoint rejects missing/expired tokens;
refresh issues a new access token; `/api/health` returns 200; tests green.

---

## Phase 2 — Domain model, dictionary CRUD, tags

**Prereqs:** Phase 1.

**Tasks**
1. `AnkiLearner.Core`: entities `Word` (incl. `LanguageCode` — spec §4.1/FR-S7), `WordTranslation`,
   `Tag`, `WordTag`, `SrsState`, exercise enum (§4). DTOs for create/update/read.
2. EF configuration: relationships, cascade deletes, unique indexes
   (`WordTranslation (WordId,LanguageCode)`, `Tag (UserId,Name)`, `SrsState (WordId,Exercise)`),
   index `Word (UserId, LanguageCode)`, `KnownLanguages` as `text[]`. Migration `DictionaryModel`.
   Word/study queries filter by the user's **current learning language**.
3. **HTML sanitization** service (e.g. `Ganss.Xss`) applied to all HTML fields on save.
4. Words endpoints (§6): list (search across term + translations, tag filter, pagination),
   get, create, update (incl. nested translations + tag assignment), delete.
5. Tags endpoints: list with counts, create, rename, delete (unlink only).
6. Settings endpoints + `GET /api/languages` (static BCP-47 catalog).
7. Tests: CRUD, search/filter, user-scoping (user A cannot see/modify user B's words), sanitizer
   strips scripts.

**Deliverables:** full dictionary + tags + settings API.

**AC:** CRUD works end-to-end; search & tag filter return correct results; cross-user access is
denied; HTML is sanitized; tests green.

---

## Phase 3 — AI word lookup (Claude Haiku) behind a provider

**Prereqs:** Phase 2.

**Tasks**
1. `AnkiLearner.Core`: `IWordLookupProvider` + `WordLookupResult` (term, transcription,
   partOfSpeech, gender, per-known-language meanings[], example, exampleTranslations) — §3.4.
2. `AnkiLearner.Infrastructure`: `ClaudeHaikuLookupProvider` using the `Anthropic` SDK with
   **structured outputs**. Generalize the POC's prompt to a configurable **target language** and
   **N known languages**; build the JSON schema dynamically from the known-language list. Model
   `claude-haiku-4-5`; config `Anthropic:ApiKey`/`Anthropic:Model`.
3. Graceful degradation: no key ⇒ provider reports unavailable; `GET /api/lookup/status`.
4. Endpoint `POST /api/lookup { term }` → result (uses caller's settings for languages; **not**
   persisted). Rate-limit it.
5. Tests: schema/prompt unit tests; provider returns "unavailable" without a key; a mocked client
   maps a sample JSON to `WordLookupResult`. (Live API calls behind an opt-in flag only.)

**Deliverables:** working AI lookup that pre-fills add-word data.

**AC:** with a key, lookup returns structured meanings/IPA/example for multiple known languages;
without a key, status is `available:false` and the rest of the app is unaffected; tests green.

---

## Phase 4 — SRS engine & study endpoints

**Prereqs:** Phase 2.

**Tasks**
1. Port **SM-2** into `AnkiLearner.Core` (`Sm2.Apply`, `Sm2.PreviewDays`) exactly per spec §5;
   pure, no I/O.
2. Study service + endpoints (§6): `counts`, `next` (due-first then new, daily-new-limit,
   optional tag filter, per-direction, **learn-ahead** for Again-cards within 20 min — FR-R9),
   `grade` (lazily create `SrsState`, apply SM-2, save, return next card + interval previews).
3. Build prompt/answer projection per direction (`TargetToKnown` vs `KnownToTarget`), aggregating
   all known-language translations on the "known" side.
4. Tests: SM-2 unit tests (each grade path, ease floor, again=+10min, preview matches apply);
   study selection order; daily-new-limit; user scoping.

**Deliverables:** complete study API.

**AC:** SM-2 matches the POC's behavior (port verified by tests); `next` returns due before new and
respects limits; `grade` advances state and persists; tests green.

---

## Phase 5 — Angular foundation: auth, shell, API layer

**Prereqs:** Phase 1 (auth API). Can start in parallel with backend Phases 2–4 against the API contract.

**Tasks**
1. App shell with Angular Material (toolbar, nav), routing, environment config for API base URL.
2. Typed API client services mirroring §6; `HttpInterceptor` to attach the JWT and **silently
   refresh** on 401; auth state in a signal-based `AuthService`; route guards.
3. `/login`, `/register` screens; logout; load `/api/auth/me` on bootstrap.
4. Global error/toast handling for RFC-7807 responses.

**Deliverables:** users can register/log in/out in the SPA; guarded routes.

**AC:** unauthenticated users are redirected to `/login`; tokens refresh transparently; reload keeps
the session (via refresh cookie).

---

## Phase 6 — Angular dictionary & add/edit word (with AI lookup)

**Prereqs:** Phases 2, 3, 5.

**Tasks**
1. `/words` list: search box, tag filter chips, pagination, row actions (edit/delete), empty state.
2. `/words/new` + `/words/:id/edit`: fields for target term, transcription, POS, gender, example,
   notes; a **translation editor per known language**; tag selector (create-on-the-fly).
3. **Look up** button → calls `/api/lookup`, fills the form (meanings rendered as an HTML list per
   language), everything editable; hide/disable the button when `lookup/status.available` is false.
   Duplicate warning (FR-W7): non-blocking hint when the normalized term already exists.
4. Safe HTML rendering (DomSanitizer/DOMPurify); HTML editing via a lightweight rich editor or a
   labeled HTML source textarea.
5. `/settings` screen: learning language, known-languages ordering, daily new limit.

**Deliverables:** full dictionary management UI incl. AI-assisted add.

**AC:** create/edit/delete words with translations + tags; search/filter work; Look up pre-fills and
remains editable; works manually when AI is unavailable.

---

## Phase 7 — Angular study/review UI

**Prereqs:** Phases 4, 5.

**Tasks**
1. `/` dashboard: per-direction due/new counts, optional tag filter, "Start" buttons.
2. `/study/:exercise`: prompt → **Show answer** → reveal (all known translations / target +
   transcription + example) → **Again/Hard/Good/Easy** buttons with interval hints; fetch next on
   grade; "all done" state.
3. **Correct my knowledge**: inline edit of the current word from the revealed answer (reuses the
   edit form/component) without losing the session.
4. Keyboard shortcuts: `Space` reveal, `1`–`4` grade (desktop); large tap targets for the four
   grade buttons on mobile — the study flow is **phone-first**.

**Deliverables:** complete study experience in both directions.

**AC:** the review loop matches AnkiDroid feel; grading advances and persists; inline correction
saves and continues; shortcuts work; the whole flow is comfortable on a phone-sized viewport.

---

## Phase 8 — Anki `.apkg` import

**Prereqs:** Phases 2 (model), 6 (UI shell for upload). Backend importer can start after Phase 2.

**Tasks**
1. `AnkiLearner.Infrastructure`: `ApkgImporter` — unzip; detect v3 via `meta` (reject legacy
   packages with a clear message — FR-I8); zstd-decompress `collection.anki21b` (`ZstdSharp.Port`);
   open with `Microsoft.Data.Sqlite` + registered `unicase` collation; read
   `notes`/`notetypes`/`fields`/`decks`/`cards`/`col`; split `flds` on `0x1f`.
   (See the POC's `CLAUDE.md` for exact format notes.)
2. Map notes → Words (§3.5): front→`Term`, back→primary-known-language `WordTranslation` (sanitized
   HTML); current learning language as `Word.LanguageCode`; deck name → tag; Anki note tags →
   tags; add `imported` tag; duplicate detection by **normalized** target term within the language.
3. **Study-progress mapping (FR-I7):** join `cards` on `nid`; `ord` 0/1 → `TargetToKnown`/
   `KnownToTarget`. Review cards (`type`=2) → `SrsState { IntervalDays=ivl, EaseFactor=factor/1000
   (0⇒2.5, floor 1.3), Lapses=lapses, Repetitions=max(2,reps), Due=col.crt+due×86400s }`; new /
   learning / suspended cards → no state. Controlled by `importProgress` (default on).
4. Endpoints: `POST /api/import/apkg` (parse + preview incl. `withProgress` count, staged by
   `importId`), `POST /api/import/apkg/{importId}/commit` `{ importDuplicates, importProgress }`
   (transactional insert, scoped to user). Enforce `Import:MaxUploadBytes`; skip-and-report
   malformed notes.
5. `/import` Angular screen: upload, preview counts (new / duplicates / with progress / skipped),
   the two options, confirm, result summary. Note in the UI that progress transfer is approximate.
6. Tests: parse a small fixture `.apkg` (include one under `AnkiLearner.Tests` fixtures);
   field-splitting, deck→tag + note-tag mapping, dup handling, malformed-note resilience,
   **progress mapping** (review/new/learning/suspended cases, ease floor, due conversion),
   legacy-format rejection.
   *(Use the POC's `All Decks.apkg` locally as a real-data smoke test — do not commit it.)*

**Deliverables:** working `.apkg` import with preview/commit, including study progress.

**AC:** a real AnkiDroid export imports with correct counts; decks + note tags become tags;
duplicates handled per the chosen option; with `importProgress` on, mature Anki cards come out
with their interval/ease/due preserved (spot-check against AnkiDroid); malformed notes are
skipped, not fatal; legacy packages are rejected with a clear message; tests green.

---

## Phase 9 — Packaging, CI/CD, production deploy

**Status: BUILT (2026-07-25), not yet deployed.** Lives in `infra/` and
`backend/AnkiLearner.Api/Dockerfile`; see [`../infra/README.md`](../infra/README.md).

Decisions taken, and why they differ from what this section originally anticipated:

| Item | Choice |
|---|---|
| Image | One image: the Angular build is baked into the API's `wwwroot`. Keeps single-origin (no CORS, the httpOnly refresh cookie keeps working) and leaves host nginx a pure proxy |
| Registry | GHCR, **public** — so the VPS holds no registry credential |
| Reverse proxy | **nginx** (not Caddy/Traefik): the box already runs it for a second site, and a shared proxy is one process, not two |
| TLS | **Cloudflare Origin CA** (15-year) + Authenticated Origin Pulls. No ACME, no renewal timer, port 80 closed |
| Deploy trigger | **Auto on push to `main`**, via `workflow_run` after CI succeeds — same as QuestionsHub. `workflow_dispatch` for manual runs |
| Deploy mechanism | Pull-based, with an instant SSH trigger pinned to a forced command. CI can say "redeploy" and nothing else |
| Runtime | Own Linux user with its **own rootless Docker daemon** — the VPS is shared with an unrelated app, and the `docker` group is root-equivalent |
| Backups | restic → OVH Object Storage, own bucket/key/password, daily |

Local development is unchanged: `docker compose up -d db` + `dotnet run` + `ng serve`.

---

## Suggested order & parallelism

```
Phase 0
  └─ Phase 1 ── Phase 2 ── Phase 3 (AI lookup)
                   │           │
                   ├── Phase 4 (SRS) ─────────┐
                   └── Phase 8 (importer) ──┐  │
Phase 5 (Angular auth, parallel after P1) ──┤  │
  ├─ Phase 6 (dictionary+lookup UI) ◀── P3  │  │
  ├─ Phase 7 (study UI) ◀── P4 ─────────────┘  │
  └─ Phase 8 UI ◀── importer ──────────────────┘
Phase 9 — deferred
```

Backend Phases 2–4 and frontend Phase 5 can proceed in parallel once the API contract (§6) is fixed.

## Definition of done (v1)

A self-hosted instance where a user can: register/log in; set learning + known languages; add words
manually or via Claude Haiku lookup; import an AnkiDroid `.apkg` **with study progress**; organize
with tags; and study both directions with SM-2 grading and inline correction, comfortably from a
phone browser — all data isolated per user, all HTML sanitized, running locally via Docker Compose
(Postgres) + the .NET API + the Angular app. CI/CD and production hosting are explicitly out of
scope for v1.
