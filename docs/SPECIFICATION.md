# AnkiLearner — Specification (v1)

Status: **complete — approved for implementation (2026-07-02)**. Authoritative for *what* v1 does. Companion docs:
`../CLAUDE.md` (project context, locked decisions) and `IMPLEMENTATION_PLAN.md` (phased build).

---

## 1. Overview

AnkiLearner is a self-hostable web application for learning a foreign language using a
spaced-repetition system (SRS), in the spirit of Anki/AnkiDroid. Users build a personal dictionary
of words/phrases in a **learning language**, optionally auto-filled by an **AI lookup** service,
and study them in both directions via SRS. The application database is the single source of truth;
Anki is only an import/export format.

The app is **generic**: the learning language and the user's known languages are per-user settings.

### 1.1 Goals (v1)

1. Multi-user accounts with isolated data.
2. Personal dictionary: create / read / update / delete words and phrases, with tags and search.
3. Add-word flow with **manual entry** and **AI-assisted lookup** (multiple meanings, IPA
   transcription, part of speech, gender, example sentence) — everything editable before save.
4. **Import** an existing AnkiDroid `.apkg` export — words, tags, **and study progress**.
5. **Study** via SRS in two directions (target→known, known→target), AnkiDroid-style confidence
   grading, with the ability to **correct the word** when the answer is shown.
6. Per-user **settings**: learning language, known languages, daily limits.
7. **Responsive UI** — the primary study device is a phone browser; every screen (especially the
   review flow) must be fully usable on a small screen.

### 1.2 Non-goals (v1) — deferred to later phases

- CI/CD pipelines, production reverse proxy, and TLS automation (Caddy/Traefik/nginx) — **deferred**.
- Game-like exercises: **word assembler** (assemble a word from letters) and **crossword**.
- Export back to `.apkg`.
- Audio / text-to-speech pronunciation.
- FSRS scheduling, per-language study directions, mobile apps, social/sharing features.
- Per-user AI keys (v1 uses a single server-level key).

These are designed *around* so they can be added without rework (see §10).

### 1.3 Primary persona

Ukrainian living in Denmark, fluent in English, learning Danish. Knows English **and** Ukrainian,
wants translations available in both. Previously used AnkiDroid; has ~945 words to import.

---

## 2. Glossary

- **Target / learning language** — the language being learned (e.g. Danish, `da`).
- **Known language** — a language the user already understands (e.g. `en`, `uk`). A user may have
  several; words may carry a translation per known language.
- **Word** — a dictionary entry: a word *or* phrase in the target language plus metadata and
  per-language translations. ("Word" and "card" are used loosely; an entry yields multiple study
  cards, one per exercise/direction.)
- **Exercise / direction** — a study mode for a word, e.g. `TargetToKnown`. SRS progress is tracked
  per `(word, exercise)`.
- **SRS** — spaced-repetition scheduling. v1 uses **SM-2**.
- **Tag** — a free-form label on a word, used for organizing and for filtering study sessions.
  Imported Anki decks become tags.

---

## 3. Functional requirements

### 3.1 Accounts & authentication

- **FR-A1** A visitor can **register** with email + password (password policy: min 8 chars).
- **FR-A2** A user can **log in** and receives a short-lived **JWT access token** plus a
  **refresh token** (httpOnly, secure cookie). The SPA refreshes access tokens silently.
- **FR-A3** A user can **log out** (refresh token revoked/cleared).
- **FR-A4** `GET /api/auth/me` returns the current user's profile + settings.
- **FR-A5** All dictionary/study/settings data is **strictly scoped to the authenticated user**;
  no endpoint may return another user's data.
- **FR-A6** Passwords are hashed via ASP.NET Core Identity (PBKDF2/Argon2 default). No plaintext.
- v1 keeps auth minimal: **no email verification, no password reset email** (documented as a known
  v1 limitation; reset can be added later). Optional: allow disabling self-registration via config
  for a single-user instance.

### 3.2 Settings

- **FR-S1** A user sets a single **learning language** (BCP-47 code, e.g. `da`).
- **FR-S2** A user sets an ordered list of **known languages** (e.g. `["en","uk"]`). The first is
  the **primary** known language (used as the default prompt side for `KnownToTarget` and the
  default target for AI translation).
- **FR-S3** A user sets a **daily new-word limit** for study (default 20; 0 = unlimited).
- **FR-S4** Settings are created with sensible defaults on registration and are editable any time.
- **FR-S5** Language lists come from a fixed built-in catalog of BCP-47 codes + display names.
- **FR-S6** Removing a known language **keeps** its stored translations in the DB (hidden in the
  UI); re-adding the language makes them visible again. No destructive side effects from settings.
- **FR-S7** Changing the learning language does **not** delete anything: words are stamped with
  their language (§4.1), and the dictionary/study views show only words matching the **current**
  learning language. Switching back restores the previous dictionary.

### 3.3 Dictionary management

- **FR-D1** List words with **pagination**, **text search** (matches target term and any
  translation), and **tag filter** (AND/OR across selected tags — v1: OR is sufficient).
  The list is implicitly scoped to the user **and** their current learning language (FR-S7);
  default sort: newest first.
- **FR-D2** View a single word with all fields and translations.
- **FR-D3** Create a word manually (see §3.4).
- **FR-D4** Edit any field of a word, including adding/removing translations and tags.
- **FR-D5** Delete a word (cascades its translations, tags links, and SRS states).
- **FR-D6** Manage tags: create, rename, delete, list with usage counts. Deleting a tag unlinks it
  from words (does not delete words).
- **FR-D7** All HTML content is **sanitized server-side** on save; rendered safely on the client.

### 3.4 Add word (manual + AI lookup)

The add-word form is always usable manually; AI lookup pre-fills it.

- **FR-W1** The user enters the **target term**. They may save immediately with manually entered
  translation(s), transcription, etc.
- **FR-W2** Clicking **"Look up"** calls the AI provider with the target term and the user's known
  languages, returning a structured result:
  - `term` — normalized lemma / dictionary form,
  - `transcription` — IPA for the target word,
  - `partOfSpeech`,
  - `gender` — article for nouns (target-language specific, e.g. `en`/`et` for Danish) or empty,
  - `meanings` — per known language, an ordered list of short meanings,
  - `example` — one natural example sentence in the **target** language,
  - `exampleTranslations` — that example translated into each known language.
- **FR-W3** The lookup result **populates the editable form**; the user can edit/remove anything
  before saving. Multiple meanings are rendered into the translation field(s) as an HTML list.
- **FR-W4** Lookup **does not auto-persist**; saving is an explicit second step.
- **FR-W5** If no server AI key is configured or the provider errors, the form still works for
  manual entry and shows a non-blocking message (**graceful degradation**).
- **FR-W6** The provider is selected behind `IWordLookupProvider`; v1 ships `ClaudeHaikuLookupProvider`.
- **FR-W7** **Duplicate warning:** if a word with the same normalized term (HTML stripped,
  trimmed, case-insensitive) already exists in the current learning language, the form shows a
  non-blocking warning with a link to the existing word. The user may still save.

### 3.5 Anki `.apkg` import

- **FR-I1** A user uploads an `.apkg` file. The backend parses it (modern Anki v3: zstd-compressed
  `collection.anki21b`, SQLite with `unicase` collation, fields split on `0x1f`).
- **FR-I2** The importer maps each note to a Word: front → target `Term`, back → a translation in
  the user's **primary** known language (content kept as sanitized HTML). Words get the user's
  current learning language (§4.1). Anki **deck name → tag**, and Anki **note tags** (the
  space-separated `notes.tags` column) → tags as well. Reversed-card notetypes still produce a
  single Word (both directions handled by SRS exercises).
- **FR-I3** The user sees a **preview** (counts: new, duplicates by target term, notes with
  progress, skipped) and confirms before committing. Import is scoped to the current user.
- **FR-I4** **Duplicate handling**: by default skip words whose **normalized** target term (HTML
  stripped, trimmed, case-insensitive) already exists for the user in the current learning
  language; option to import anyway. Imported words get an `imported` tag + the deck/note tags.
- **FR-I5** Import is resilient: a malformed note is skipped and reported, not fatal.
- **FR-I6** Reasonable size limit (e.g. 50 MB upload) and the parse runs server-side only.
- **FR-I7** **Study-progress import** (option on the commit step, default **on**): Anki's per-card
  scheduling state is mapped best-effort onto `SrsState`. Anki `cards` rows join notes via `nid`;
  `ord` 0 → `TargetToKnown`, `ord` 1 → `KnownToTarget` (reversed notetypes). Per card:
  - **review cards** (`type` = 2): `IntervalDays = ivl` (positive = days), `EaseFactor =
    factor / 1000` (if 0 ⇒ 2.5; floor 1.3), `Lapses = lapses`, `Repetitions = max(2, reps)` so the
    next successful review multiplies the interval instead of restarting the 1d/6d ladder,
    `Due = col.crt + due × 86400 s` (`due` for review cards is days since collection creation;
    past dates simply mean "due now"), `LastReviewed = null`.
  - **new** (`type` = 0) and **learning** (`type` = 1/3) cards: no `SrsState` — treated as new.
  - **suspended** (`queue` = -1): word imported, no `SrsState`.
  The mapping is approximate by design and documented in the UI ("progress is carried over
  approximately"). With the option off, all imported words start as new.
- **FR-I8** **Format detection:** v1 supports the modern v3 package (`collection.anki21b`,
  zstd). Legacy packages (plain `collection.anki21`/`.anki2` without the v3 meta) are detected and
  rejected with a clear message telling the user to re-export from a current AnkiDroid/Anki
  version. (Legacy support may be added later.)

### 3.6 Study / review (SRS)

- **FR-R1** The home/study screen shows, per direction, counts of **due** and **new** cards
  (optionally filtered by tag), like the POC's exercise picker.
- **FR-R2** The user picks a **direction**:
  - `TargetToKnown` — prompt = target term; answer reveals all known-language translations.
  - `KnownToTarget` — prompt = primary known-language translation(s); answer reveals the target
    term (+ transcription, example).
- **FR-R3** The review flow is AnkiDroid-style: show prompt → **"Show answer"** → reveal →
  four grade buttons **Again / Hard / Good / Easy**, each annotated with the resulting interval.
- **FR-R4** Grading applies **SM-2** (§5) to that `(word, exercise)` SRS state and advances to the
  next card. Card selection: **due reviews first** (oldest `Due`), then **new** words (respecting
  the daily new limit), then nothing-due state.
- **FR-R5** **Correct my knowledge**: from the revealed answer the user can open an inline editor to
  fix the word (translation/transcription/etc.) without leaving the session.
- **FR-R6** Keyboard shortcuts: `Space` = show answer; `1`–`4` = Again/Hard/Good/Easy.
- **FR-R7** New cards respect the **daily new-word limit** (`UserSettings.DailyNewLimit`).
- **FR-R8** A word with no SRS state for a direction is treated as **new** for that direction (state
  is created lazily on first grade), exactly as in the POC.
- **FR-R9** **Learn-ahead:** when nothing else is due and the daily new limit is exhausted, but
  "Again" cards are scheduled within the next **20 minutes**, they are served early so a session
  can be finished (Anki-style). Study counts include such cards.
- **FR-R10** Study queries are scoped to the current learning language (FR-S7); the direction
  labels in the UI use the user's actual language names (e.g. "Danish → English/Ukrainian").

### 3.7 AI provider abstraction

- **FR-P1** `IWordLookupProvider` defines `Task<WordLookupResult> LookupAsync(term, targetLang,
  knownLangs, ct)`.
- **FR-P2** v1 implements `ClaudeHaikuLookupProvider` using the `Anthropic` SDK with **structured
  outputs** (JSON schema), reusing the POC's prompt design (generalized to N known languages and a
  configurable target language).
- **FR-P3** Provider config (API key, model) is read from server config; missing key ⇒ provider
  reports "unavailable" and callers degrade gracefully.

---

## 4. Data model

PostgreSQL via EF Core. All entity tables (except Identity + language catalog) carry a `UserId`
FK (directly or transitively) and are filtered by it. Timestamps in UTC.

### 4.1 Entities

**User** — ASP.NET Core Identity user (`Id` GUID, `Email`, `PasswordHash`, …).

**UserSettings** (1:1 with User)
| field | type | notes |
|---|---|---|
| UserId | GUID PK/FK | |
| LearningLanguage | text | BCP-47, e.g. `da` |
| KnownLanguages | text[] (or JSON) | ordered; first = primary |
| DailyNewLimit | int | default 20; 0 = unlimited |
| CreatedAt / UpdatedAt | timestamptz | |

**Word** (owned by User)
| field | type | notes |
|---|---|---|
| Id | GUID PK | |
| UserId | GUID FK | scoping |
| LanguageCode | text | the word's target language (user's learning language at creation) |
| Term | text (HTML, sanitized) | target-language word/phrase |
| Transcription | text? | IPA |
| PartOfSpeech | text? | |
| Gender | text? | article/gender marker, target-specific |
| Example | text? (HTML) | example sentence in target language |
| Notes | text? (HTML) | |
| CreatedAt / UpdatedAt | timestamptz | |
| (nav) Translations, Tags, SrsStates | | |

**WordTranslation**
| field | type | notes |
|---|---|---|
| Id | GUID PK | |
| WordId | GUID FK | cascade delete |
| LanguageCode | text | BCP-47, one of user's known languages |
| Text | text (HTML, sanitized) | meanings (may be an HTML list) |
| ExampleTranslation | text? (HTML) | translation of `Word.Example` |
| **unique** (WordId, LanguageCode) | | |

**Tag** (owned by User)
| field | type | notes |
|---|---|---|
| Id | GUID PK | |
| UserId | GUID FK | |
| Name | text | **unique** (UserId, Name) |

**WordTag** (M:N) — `(WordId, TagId)` composite PK, both cascade.

**SrsState** (SM-2 state per word+exercise)
| field | type | notes |
|---|---|---|
| Id | GUID PK | |
| WordId | GUID FK | cascade delete |
| Exercise | text | enum stored as string: `TargetToKnown`, `KnownToTarget`, (later `Typing`,`Anagram`) |
| Due | timestamptz | next review |
| IntervalDays | int | current interval |
| EaseFactor | double | start 2.5, min 1.3 |
| Repetitions | int | consecutive successes |
| Lapses | int | times forgotten |
| LastReviewed | timestamptz? | |
| **unique** (WordId, Exercise) | | |

### 4.2 Notes on the model

- `KnownLanguages` may be a `text[]` (Npgsql supports it) or a small child table; `text[]` is
  simplest and is fine for v1.
- `Word.LanguageCode` makes switching the learning language non-destructive: all dictionary and
  study queries filter `(UserId, LanguageCode = settings.LearningLanguage)`. Index on
  `(UserId, LanguageCode)`.
- Multiple meanings are stored as formatted HTML inside `WordTranslation.Text` (round-trips with
  Anki and matches the POC). A normalized "meanings" child table is a possible future refinement.
- SRS exercise enum is open for the future game exercises without schema change.

---

## 5. SRS algorithm (SM-2) — exact spec

Ported from the POC (`DanishLearner/src/Services/Sm2.cs`). Buttons map to SM-2 quality:

| Button | Grade | Quality |
|---|---|---|
| Again | 0 | 2 |
| Hard | 1 | 3 |
| Good | 2 | 4 |
| Easy | 3 | 5 |

On grade `q` for state `s` at time `now`:

```
if q < 3:                      # forgot
    s.Repetitions = 0
    s.IntervalDays = 0
    s.Lapses += 1
else:
    s.Repetitions += 1
    s.IntervalDays = 1                         if Repetitions == 1
                     6                          if Repetitions == 2
                     round(IntervalDays * Ease) otherwise   (min 1)

s.EaseFactor += 0.1 - (5-q)*(0.08 + (5-q)*0.02)
if s.EaseFactor < 1.3: s.EaseFactor = 1.3

s.Due = now + 10 minutes    if q < 3
        now + IntervalDays   otherwise
s.LastReviewed = now
```

- **Interval preview** for each button (shown on the button) is computed by applying the rule to a
  copy of the state without mutating it.
- "Again" reschedules within the session (+10 min) without corrupting the day interval.
- A missing state is treated as a fresh `SrsState` (Ease 2.5, Interval 0, Reps 0).

---

## 6. API surface (REST, all under `/api`, JSON)

Auth via `Authorization: Bearer <jwt>` except register/login/refresh. All list endpoints paginate.

**Auth**
- `POST /api/auth/register` `{ email, password }` → 201 + tokens
- `POST /api/auth/login` `{ email, password }` → `{ accessToken }` (+ refresh cookie)
- `POST /api/auth/refresh` (cookie) → `{ accessToken }`
- `POST /api/auth/logout` → 204
- `GET  /api/auth/me` → `{ user, settings }`

**Settings**
- `GET /api/settings`
- `PUT /api/settings` `{ learningLanguage, knownLanguages[], dailyNewLimit }`
- `GET /api/languages` → catalog `[{ code, name }]`

**Words**
- `GET    /api/words?search=&tag=&page=&pageSize=` → paged words
- `GET    /api/words/{id}`
- `POST   /api/words` (create; body includes translations[] + tags[])
- `PUT    /api/words/{id}`
- `DELETE /api/words/{id}`

**Tags**
- `GET    /api/tags` → `[{ id, name, count }]`
- `POST   /api/tags` `{ name }`
- `PUT    /api/tags/{id}` `{ name }`
- `DELETE /api/tags/{id}`

**Lookup (AI)**
- `GET  /api/lookup/status` → `{ available: bool, provider }`
- `POST /api/lookup` `{ term }` → `WordLookupResult` (not persisted)

**Study**
- `GET  /api/study/counts?tag=` → per-exercise `{ due, new }`
- `GET  /api/study/next?exercise=&tag=` → `{ word, prompt, answerHidden, intervals{Again,Hard,Good,Easy}, remaining }`
- `POST /api/study/grade` `{ wordId, exercise, grade }` → applies SM-2, returns next card

**Import**
- `POST /api/import/apkg` (multipart file) → preview `{ importId, total, new, duplicates, withProgress, skipped[] }`
- `POST /api/import/apkg/{importId}/commit` `{ importDuplicates: bool, importProgress: bool }` → `{ imported, statesImported }`

All endpoints validate input and return RFC-7807 problem responses on error.

---

## 7. Frontend (Angular)

- **Stack:** latest stable Angular, standalone components, signals for local/UI state, `HttpClient`
  + typed API services, Angular Material, route guards, a JWT `HttpInterceptor` (attach token,
  refresh on 401). UI strings in English, structured for future i18n.
- **Routes / screens:**
  - `/login`, `/register`
  - `/` — study dashboard: per-direction due/new counts, tag filter, start buttons.
  - `/study/:exercise` — review flow (prompt → reveal → grade), inline "correct" editor,
    keyboard shortcuts.
  - `/words` — dictionary list: search, tag filter, pagination, row actions.
  - `/words/new`, `/words/:id/edit` — add/edit form with **Look up** button and editable fields,
    translations per known language, tags.
  - `/import` — upload `.apkg`, preview, confirm.
  - `/settings` — learning language, known languages, daily limit.
- **HTML safety:** render word HTML via Angular sanitization (and/or DOMPurify); edit via a
  lightweight rich-text editor or a clearly-labeled HTML textarea for v1.
- **Responsive / mobile-first:** the primary study device is a phone browser. Every screen must
  work on small viewports; the review screen in particular gets large tap targets for the four
  grade buttons and no horizontal scrolling.
- **Code style:** the project owner is a .NET developer **without Angular/TypeScript experience**.
  Frontend code must stay simple and idiomatic: standard Angular patterns only, no clever TS
  tricks, small components, comments where an Angular concept would surprise a C# developer.

---

## 8. Non-functional requirements

- **Security:** per-user data isolation enforced server-side on every query; sanitized HTML;
  JWT with short expiry + refresh; secrets only via config/env, never committed; rate-limit
  auth + lookup endpoints.
- **Privacy/cost:** AI lookup sends the target term (and known-language codes) to Anthropic using
  the operator's key; documented. No content sent without an explicit "Look up" action.
- **Performance:** dictionary list and study-next are indexed (Word.UserId, SrsState (Exercise,Due),
  unique indexes per §4). Study-next is a single ordered query.
- **Reliability:** EF Core migrations applied on startup; import is transactional per commit.
- **Portability/generic:** no hard-coded language; Danish-specifics (e.g. `en/et` gender) come from
  the AI prompt per target language, not from app logic.
- **Testability:** SM-2 engine and `.apkg` parser are pure/unit-testable; API has integration tests
  against a disposable Postgres (Testcontainers) or in-memory provider where adequate.
- **Observability:** structured logging; health endpoint `GET /api/health`.
- **Usability:** fully responsive (phone-first for the study flow); keyboard shortcuts on desktop.

---

## 9. Configuration

| Key | Purpose | Default / notes |
|---|---|---|
| `ConnectionStrings:Default` | Postgres connection | required |
| `Anthropic:ApiKey` (`ANTHROPIC_API_KEY`) | AI lookup key | optional; absent ⇒ AI disabled |
| `Anthropic:Model` | lookup model | `claude-haiku-4-5` |
| `Jwt:Issuer/Audience/SigningKey` | token signing | required (generate per instance) |
| `Auth:AllowRegistration` | enable self-signup | `true` |
| `Import:MaxUploadBytes` | `.apkg` size cap | e.g. 52428800 (50 MB) |

---

## 10. Future features (design-around, not built in v1)

- **Word assembler** and **crossword** — new `Exercise` enum values + new study UIs; SRS model
  already supports per-exercise state.
- **Export to `.apkg`** (native C# writer) — inverse of the importer.
- **Audio / TTS** pronunciation, FSRS scheduler, per-language study directions.
- **Per-user AI keys**, additional providers (DeepL/Google) via the existing `IWordLookupProvider`.
- **CI/CD + production deploy** (Docker images, registry, reverse proxy + TLS) — deferred phase.
