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
- **FR-D8** **Reset a word's progress**: drop its SRS states in *all* directions so it is offered
  as new again, keeping the word, its translations and its tags. Confirmed in the UI before it runs.
  **Relearn** ("Study again", for a word the user forgot) is the gentler sibling: every direction
  becomes **due now**. An existing state is treated as a lapse (`Repetitions = 0`,
  `IntervalDays = 0`, `Lapses + 1`, ease kept, `Due = min(Due, now)` so an overdue card keeps its
  place; `Sm2.Relearn`); a direction never studied gets a new state due
  now, which counts toward today's new-word allowance (FR-R7). Unlike a reset, the word does not
  rejoin the new-card queue, where newest-first ordering (FR-R4) would put an old word last.
  Not destructive, so no confirmation: a **Study again** button sits next to Reset progress on
  the word edit form, and on Translate cards for words already in the dictionary (§7).

### 3.4 Add word (manual + AI lookup)

The add-word form is always usable manually; AI lookup pre-fills it.

- **FR-W1** The user enters the **target term**. They may save immediately with manually entered
  translation(s), transcription, etc.
- **FR-W2** Clicking **"Look up"** calls the AI provider with the target term and the user's known
  languages, returning a structured result:
  - `term` — the **citation form**. For a language with a particle table (FR-W7; Danish today)
    nouns carry their indefinite article and verbs the infinitive marker ("en aftale", "et minde",
    "at spise"), which duplicate matching then ignores; every other language keeps the plain lemma
    (base/dictionary form). Translate (§3.8) uses the same rule, so both ways of adding a word agree,
  - `transcription` — IPA for the target word itself (for a particle language: without the article
    or infinitive marker),
  - `partOfSpeech`,
  - `gender` — article for nouns (target-language specific, e.g. `en`/`et` for Danish) or empty;
    still filled although the term carries the article,
  - `meanings` — per known language, an ordered list of short meanings,
  - `example` — one natural example sentence in the **target** language,
  - `exampleTranslations` — that example translated into each known language.
- **FR-W3** The lookup result **populates the editable form**; the user can edit/remove anything
  before saving. Multiple meanings are rendered into the translation field(s) as an HTML list.
- **FR-W4** Lookup **does not auto-persist**; saving is an explicit second step.
- **FR-W5** If no server AI key is configured or the provider errors, the form still works for
  manual entry and shows a non-blocking message (**graceful degradation**).
- **FR-W6** The provider is selected behind `IWordLookupProvider`; v1 ships `ClaudeLookupProvider`.
- **FR-W7** **Duplicate warning:** if a word with the same normalized term (HTML stripped,
  trimmed, case-insensitive) already exists in the current learning language, the form shows a
  non-blocking warning with a link to the existing word. The user may still save.
  Matching ignores **one leading particle** from a per-language table — Danish `en`/`et`/`at`, so
  "aftale" finds "en aftale" and "at spise" finds "spise" — only when a space and more text follow
  it ("enkel", a bare "en" are unchanged). Languages without a table match exactly. An exact match
  wins over an article variant, then the oldest word, then the lowest id (imported words share one
  creation time).

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

- **FR-R1** The study screen (`/study`) shows, per direction, counts of **due** and **new** cards plus
  the number of words **reviewed today** (optionally filtered by tag), like the POC's exercise
  picker. "Today" is the **UTC day**, the same boundary FR-R7 uses. The counter reads each card's
  latest review (`SrsState.LastReviewed`), not the review log (FR-R11), so repeat presses on one
  word count once and FR-D8 removes it from the total.
- **FR-R2** The user picks a **direction**:
  - `TargetToKnown` — prompt = target term; answer reveals all known-language translations.
  - `KnownToTarget` — prompt = primary known-language translation(s); answer reveals the target
    term (+ transcription, example).
- **FR-R3** The review flow is AnkiDroid-style: show prompt → **"Show answer"** → reveal →
  four grade buttons **Again / Hard / Good / Easy**, each annotated with the resulting interval.
- **FR-R4** Grading applies **SM-2** (§5) to that `(word, exercise)` SRS state and advances to the
  next card. Card selection: **due reviews first** (oldest `Due`), then **new** words, **newest
  first** so words met today come up next (respecting the daily new limit), then nothing-due state.
- **FR-R5** **Correct my knowledge**: from the revealed answer the user can open an inline editor to
  fix the word (translation/transcription/etc.) without leaving the session.
- **FR-R6** Keyboard shortcuts: `Space` = show answer; `1`–`4` = Again/Hard/Good/Easy.
- **FR-R7** New cards respect the **daily new-word limit** (`UserSettings.DailyNewLimit`).
- **FR-R8** A word with no SRS state for a direction is treated as **new** for that direction (state
  is created lazily on first grade, or by Relearn, FR-D8), exactly as in the POC.
- **FR-R9** **Learn-ahead:** when nothing else is due and the daily new limit is exhausted, but
  "Again" cards are scheduled within the next **20 minutes**, they are served early so a session
  can be finished (Anki-style). Study counts include such cards.
- **FR-R10** Study queries are scoped to the current learning language (FR-S7); the direction
  labels in the UI use the user's actual language names (e.g. "Danish → English/Ukrainian").
- **FR-R11** **Review log:** every grade press (in-session repeats included), every Relearn and
  every Reset (FR-D8) appends one row per direction to `ReviewLog`, in the same transaction as the
  state change: kind, grade, time, the card's previous review time, and interval / ease / due
  before and after. Rows are never updated. A reset keeps the history; deleting the word deletes
  it. Not exposed in the API or UI yet; it exists so the scheduler can be fitted to and replayed
  over the user's real history (ADR-0012).

### 3.7 AI provider abstraction

- **FR-P1** `IWordLookupProvider` defines `Task<WordLookupResult> LookupAsync(term, targetLang,
  knownLangs, ct)` and `Task<TranslateResult> TranslateAsync(text, sourceLang?, targetLang,
  knownLangs, ct)` (§3.8). Lookup and Translate share one card schema and one citation-form rule.
- **FR-P2** v1 implements `ClaudeLookupProvider` using the `Anthropic` SDK with **structured
  outputs** (JSON schema), reusing the POC's prompt design (generalized to N known languages and a
  configurable target language).
- **FR-P3** Provider config (API key, model) is read from server config; missing key ⇒ provider
  reports "unavailable" and callers degrade gracefully.
- **FR-P4** Every AI call has a total time limit (`Anthropic:TimeoutSeconds`, SDK retries
  included). A timeout, a cancellation the client did not cause, or an answer cut off by
  `max_tokens` or ended by a `refusal` is a provider failure (502) — for lookup and Translate alike. A failure is logged with its exception and the text length. The user's text and
  the model's output are never logged (malformed JSON is reported without quoting it); an error
  body returned by the provider's API may be.

### 3.8 Translate

A second mode beside studying: the user types a word, phrase or sentence in the learning language
or a known language, sees its translation, and adds learning-language vocabulary from it.
`POST /api/translate` calls `IWordLookupProvider.TranslateAsync`; the deterministic rules below
live in the pure `TranslateShaper` (Core), because the AI output is untrusted.

- **FR-T1 Input.** At most **300 characters** (one sentence) **after cleaning**: the server
  removes invisible format characters (Unicode category Cf: soft hyphen, direction marks,
  zero-width characters, BOM), turns control characters (Cc, e.g. NUL) into spaces, collapses
  every whitespace run, newlines included, to one space, and trims. The raw request may be up to 1000 characters, so a
  pasted sentence with line breaks still fits. Empty or over 300 after cleaning ⇒ 400.
- **FR-T2 Source language.** Auto-detected among the learning language and the known languages;
  a word that exists in several of them (Danish/English "gift", "barn") is taken as the learning
  language. The user may **override** it with one of their own languages (codes are
  case-insensitive; any other code ⇒ 400); the override wins over detection. It travels in the
  user message, never in the JSON schema, so a user's schema is always the same (each new schema
  costs an API-side compile, cached 24 h).
  Input in **any other language** ⇒ **422** "Only Danish, English and Ukrainian are supported."
  (names from the user's settings).
- **FR-T3 Result.** `sourceLanguage`; `inputKind` — `Word` / `Phrase` / `Sentence`, as the model
  classifies it (a sentence can be one token: "Tak!", or a Japanese sentence; an unrecognised kind
  falls back to `Word` for input without whitespace, else `Phrase`); `inputTranslations` — the
  whole input in the learning language and every known language, the source slot holding the
  user's original; and **cards**, which are always learning-language entries, usually 1–3,
  **at most 5** (enforced by the server; the schema cannot cap arrays):
  - a learning-language word ⇒ its entry — **one card per citation form**, listing all its senses
    in `meanings`; separate cards only when the citation forms differ ("gift" / "en gift");
  - a known-language word or phrase ⇒ its learning-language equivalents, most common first, each
    with a note on how it differs (know → vide / kende / kunne);
  - a learning-language phrase or any sentence ⇒ the words and expressions worth learning from
    the learning-language text; trivial function words skipped, idioms and fixed expressions kept
    whole.

  Each card has the FR-W2 fields — citation-form `term`, `transcription`, `partOfSpeech`,
  `gender`, `meanings`, `example`, `exampleTranslations` — plus a one-sentence `note` in the
  first known language. The prompt asks for up to 3 meanings per known language; the server
  keeps at most 4 (room for merged senses, below). The term is made plain text (HTML stripped and
  decoded, whitespace collapsed, case kept); a card whose term is then empty is dropped. Cards are deduplicated by **exact** normalized term, so homographs ("gift"
  married / "en gift" poison) both survive; a repeated term is the same citation form, so its
  meanings are merged into the first card (distinct, in order, at most 4 per language) and the
  rest of it is dropped. Transcription, part of speech and gender are clamped to the `Word` column
  limits; every known language is present in `meanings` and `exampleTranslations`.
- **FR-T4 Sentence examples.** For sentence input every card's example is the user's own sentence
  — the best memory hook: the input itself when it is in the learning language, otherwise its
  learning-language translation with the original as that language's example translation. The
  other example translations are the `inputTranslations`. (The AI writes no examples for
  sentences, which also keeps the call fast.) Without a learning-language rendering the model's
  examples are kept.
- **FR-T5 Whole input.** Multi-word `Phrase` and `Sentence` input also returns `wholeInput`: a
  card whose term is the learning-language text (the input, or its translation) and whose
  meanings are the known-language renderings; no transcription, part of speech, gender, example
  or note. Input without whitespace ("Tak!") never gets one, whatever its kind: its card is the
  entry. Trailing sentence punctuation and closing quotes/brackets (`. ! ? …` `" ” » ' ’ ) ]`) do
  not make a different entry: a phrase's term drops them ("Tak for mad!" → "Tak for mad"), a
  sentence keeps them as typed. Omitted when there is no learning-language rendering or a card is
  already the same entry (same match key, FR-W7, trailing punctuation ignored).
- **FR-T6 Existing words.** Each card and `wholeInput` carries `existingWordId` when the user's
  dictionary already has it in the current learning language, matched as in FR-W7 (one leading
  particle ignored, exact match first, then the oldest word, then the lowest id); `wholeInput` is
  looked up both as typed and without trailing punctuation. A saved word that **exactly** matches
  one entry is never a looser match for another entry of the same response: with only "en gift"
  (poison) saved, the "gift" (married) card stays unflagged so it can be added. One indexed query
  per translation, scoped by user and learning language.
- **FR-T7 No persistence.** Translate stores nothing and keeps no history; adding a card is the
  client's explicit `POST /api/words`.
- **FR-T8 Limits and failures.** Translate shares the per-user `lookup` rate limit with FR-W2 (one
  AI budget; 429 when exceeded). Not configured ⇒ 503; provider failure (FR-P4) ⇒ 502.

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
| MaxIntervalDays | int | SRS interval ceiling; default 36500 |
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

**ReviewLogEntry** (table `ReviewLog`; append-only history, FR-R11)
| field | type | notes |
|---|---|---|
| Id | bigint PK (identity) | insertion order breaks timestamp ties |
| WordId | GUID FK | cascade delete |
| Exercise | text | as in SrsState |
| Kind | text | `Review`, `Relearn`, `Reset` |
| Source | text | `Site`, `Anki` (imported revlog) |
| Grade | text? | `Again`/`Hard`/`Good`/`Easy`; null for Relearn/Reset |
| ReviewedAt | timestamptz | event time |
| PreviousReviewAt | timestamptz? | the card's `LastReviewed` before the event |
| IntervalBefore / EaseBefore / DueBefore | int? / double? / timestamptz? | null if no state existed |
| IntervalAfter / EaseAfter / DueAfter | int? / double? / timestamptz? | null after a reset |
| index (WordId, Exercise, ReviewedAt) | | |

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

## 5. SRS algorithm (SM-2 + Anki grade modifiers) — exact spec

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
    previous = s.IntervalDays
    s.Repetitions += 1

    # Baseline for a "Good" answer: the SM-2 ladder.
    good = 1                       if Repetitions == 1
           6                       if Repetitions == 2
           previous * Ease         otherwise

    days = min(good, max(1, previous * 1.2))   if q == 3   # Hard
           4                                    if q == 5 and Repetitions == 1   # Easy, new card
           good * 1.3                           if q == 5                        # Easy
           good                                 otherwise                        # Good

    s.IntervalDays = clamp(round(days), 1, MaxIntervalDays)

s.EaseFactor += 0.1 - (5-q)*(0.08 + (5-q)*0.02)
if s.EaseFactor < 1.3: s.EaseFactor = 1.3

s.Due = now + 10 minutes    if q < 3
        now + IntervalDays   otherwise
s.LastReviewed = now
```

- **Why the Hard/Easy modifiers.** Textbook SM-2 derives the interval from the ease factor alone,
  so Hard/Good/Easy all produce the *same* next interval and only diverge one review later, when
  the changed ease is finally used. That makes the three button hints identical on every mature
  card. We keep the SM-2 ladder as the Good baseline and add Anki's **hard factor (1.2)**,
  **easy bonus (1.3)** and **easy interval (4 d)** so the buttons differ when they are pressed.
  Ease still moves by the SM-2 formula.
- **`MaxIntervalDays`** is a per-user setting (`UserSettings`, default 36500 — Anki's default).
  It caps every passing grade; "Again" is unaffected.
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
- `PUT /api/settings` `{ learningLanguage, knownLanguages[], dailyNewLimit, maxIntervalDays? }`
  (`maxIntervalDays` is optional; omitting it leaves the stored value unchanged)
- `GET /api/languages` → catalog `[{ code, name }]`

**Words**
- `GET    /api/words?search=&tag=&page=&pageSize=` → paged words
- `GET    /api/words/{id}`
- `POST   /api/words` (create; body includes translations[] + tags[])
- `PUT    /api/words/{id}`
- `DELETE /api/words/{id}`
- `DELETE /api/words/{id}/progress` → 204; resets the word's SRS states in every direction
- `POST   /api/words/{id}/relearn` → 204; makes the word due now in every direction (FR-D8)
- `GET    /api/words/duplicate?term=&excludeId=` → `{ exists, wordId }` (FR-W7)

**Tags**
- `GET    /api/tags` → `[{ id, name, count }]`
- `POST   /api/tags` `{ name }`
- `PUT    /api/tags/{id}` `{ name }`
- `DELETE /api/tags/{id}`

**Lookup (AI)**
- `GET  /api/lookup/status` → `{ available: bool, provider }`
- `POST /api/lookup` `{ term }` → `WordLookupResult` (not persisted)
- `POST /api/translate` `{ text, sourceLanguage? }` → `{ sourceLanguage, inputKind,
  inputTranslations, cards[], wholeInput? }`, each card with `existingWordId?` (not persisted;
  shares the lookup rate limit; 400 / 422 / 502 / 503 per §3.8)

**Study**
- `GET  /api/study/counts?tag=` → per-exercise `{ due, new, reviewedToday }`
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
  - `/login`, `/register` — after success, go to the `returnUrl` query parameter if it is safe,
    otherwise to `/` (see **Sign-in return** below). Each page's link to the other keeps it.
  - `/` — home, a neutral **mode selector**: two large cards, each one link (stacked on phones,
    side by side from 600 px). **Translate** (→ `/translate`) names the user's languages and
    shows "AI is not configured on this server" when lookup is unavailable (still a link).
    **Learn** (→ `/study`) shows the study backlog: due and new summed over both directions
    without a tag filter ("12 due · 5 new"), or "Nothing due right now" when both are 0 (also
    the case for an empty dictionary, and while "Again" cards are minutes away). A visually
    hidden `<h1>` ("Choose a mode") names the page for screen readers.
  - `/study` — study dashboard: per-direction due/new/reviewed-today counts, tag filter, start
    buttons.
  - `/study/:exercise` — review flow (prompt → reveal → grade), inline "correct" editor,
    keyboard shortcuts.
  - `/translate` — Translate mode (§3.8): a phone-friendly input (Enter or the keyboard's Go
    translates, Shift+Enter breaks the line; no autocorrect/autocapitalize; 300 characters) and a
    language toggle (Auto + the user's languages; changing it re-translates a shown result).
    Shows the detected language, the input in the other languages, and one card per suggested
    word (+ the whole multi-word input), all AI text rendered as plain text. **Add** saves a card
    in one tap (`POST /api/words`, HTML fields escaped, empty languages left out, tag
    `translated`) and offers **Edit** in the toast; a word already in the dictionary shows
    "In your dictionary", **Study again** (FR-D8) and **Add anyway**. `?q=<text>` translates
    `text` on arrival (URLs and one matching pair of wrapping quotes stripped), and so does text
    shared from another Android app (see **Installable app** below). The page then drops `q`,
    `title` and `url` from the URL so Back or a reload doesn't repeat the paid call. The last
    result (with each card's status) is kept in memory, so returning to the page shows it without a new call; another user signing in on
    the same tab, or a change of languages, starts a clean page. AI not configured ⇒ a
    message and a link to the manual add form; 422 ⇒ the server's message and a hint to pick
    the language by hand.
  - `/words` — dictionary list: search, tag filter, pagination, row actions.
  - `/words/new`, `/words/:id/edit` — add/edit form with **Look up** button and editable fields,
    translations per known language, tags; for an existing word, **Study again** and
    **Reset progress** (FR-D8).
  - `/import` — upload `.apkg`, preview, confirm. Reached from the Dictionary page ("Import from
    Anki", next to "Add word"), not the toolbar.
  - `/settings` — learning language, known languages, daily limit.
- **Toolbar:** the brand (→ `/`), then **Translate**, **Learn** (`/study`), **Dictionary**,
  **Settings** and **Sign out**. The current section gets a tinted pill and `aria-current="page"`;
  Learn stays active during a review (`/study/:exercise`) and nothing is active on `/`. Up to
  700 px the links are icon-only (each keeps an `aria-label`), 44 px wide with a 48 px tall
  touch target and a 4 px gap. Below 360 px the brand shrinks to a home icon, so the bar fits
  a 320 px screen without horizontal scrolling.
- **Sign-in return (`returnUrl`):** the route guard sends an anonymous user to
  `/login?returnUrl=<requested URL>`, so a shared `/translate?q=…` link survives the sign-in.
  An expired session (failed refresh) does the same with the current URL, so signing in again
  keeps the current page; the Translate page has already removed `?q=` by then, but shows its
  last text again from its in-memory state. Home adds no `returnUrl`, a plain sign-out goes to
  `/login` without one, and an expiry while on `/login` or `/register` stays there. Login and
  register use the value only when it is an in-app path: it starts with exactly one `/`, has
  no backslash, whitespace or control characters, decodes cleanly, and is not `/login` or
  `/register` itself (`core/safe-return-url.ts`). Anything else (`//host`, `/\host`, `https:`,
  `javascript:`, `/%` …) is ignored and the user lands on `/`, so the parameter can't be used
  as an open redirect or a sign-in loop. The navigation after success replaces the sign-in
  page in the browser history, so Back doesn't return to the form.
- **HTML safety:** render word HTML via Angular sanitization (and/or DOMPurify); edit via a
  lightweight rich-text editor or a clearly-labeled HTML textarea for v1.
- **Responsive / mobile-first:** the primary study device is a phone browser. Every screen must
  work on small viewports; the review screen in particular gets large tap targets for the four
  grade buttons and no horizontal scrolling.
- **Installable app (PWA):** `public/manifest.webmanifest`, linked from `index.html`, lets Chrome
  on Android install the site to the home screen: `id`, `start_url` and `scope` `/`, standalone
  display, `theme_color`/`background_color` `#faf9fd` (the toolbar's surface colour, also the
  `theme-color` meta tag), icons 192 and 512 px plus a 512 px maskable one, and long-press
  shortcuts **Translate** (`/translate`) and **Learn** (`/study`). The installed app is a
  **share target**: selecting text in any app → Share → AnkiLearner opens
  `GET /translate?title=…&q=…&url=…` (the shared `text` arrives as `q`). The Translate page uses
  the first of `q`, `title`, `url` that still has text after cleaning — Android puts the
  selection in `text`, in quotes and followed by a link to its page — and then removes all three
  from the URL. The usual guards apply: a share that arrives while a translation runs is ignored,
  and one whose result is already shown makes no new call. There is **no service worker**:
  Chrome no longer requires one to install, and without one a deploy can't leave a stale cached
  app behind. The icon sources are SVGs next to the PNGs (`public/icons/`). Caching: the API
  sends `public, max-age=31536000, immutable` only for the content-hashed `.js`/`.css`; every
  other file (index.html — also for deep links via the fallback — the manifest, icons, favicon)
  gets `no-cache`, so it is revalidated (ETag) on each use.
- **Code style:** the project owner is a .NET developer **without Angular/TypeScript experience**.
  Frontend code must stay simple and idiomatic: standard Angular patterns only, no clever TS
  tricks, small components, comments where an Angular concept would surprise a C# developer.

---

## 8. Non-functional requirements

- **Security:** per-user data isolation enforced server-side on every query; sanitized HTML;
  JWT with short expiry + refresh; secrets only via config/env, never committed; rate-limit
  auth + AI (lookup, translate) endpoints.
- **Privacy/cost:** AI lookup and Translate send the user's text (the term, or the word, phrase or
  sentence to translate) and language codes to Anthropic using the operator's key; documented.
  Content is sent only on an explicit **Look up** or **Translate** action — including a translate
  deep link (`/translate?q=`) the user opens, or text they share to the installed app. Neither
  the text nor the AI output is logged.
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
| `Anthropic:ApiKey` (`ANTHROPIC_API_KEY`) | AI lookup + translate key | optional; absent ⇒ AI disabled |
| `Anthropic:Model` | lookup + translate model | `claude-haiku-5-5` |
| `Anthropic:Effort` | thinking depth; `default` ⇒ not sent | `low` |
| `Anthropic:MaxTokens` | output cap per AI call, thinking included (only a ceiling) | `4096` |
| `Anthropic:TimeoutSeconds` | total time limit per AI call, retries included | `30` |
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
