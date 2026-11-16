# Architecture Decision Records

> One record per significant decision or feature: the context, what was chosen, and what it costs.
> Append new records at the bottom with the next number. Never rewrite an accepted record. Supersede
> it with a new one and set the old one's status to "Superseded by ADR-NNNN". Defects to fix later go
> in [`BACKLOG.md`](BACKLOG.md), not here. Records 0001–0005 were written retroactively (2026-11-16)
> from the project history.

| # | Date | Title | Status |
|---|------|-------|--------|
| [0001](#adr-0001--v1-foundation) | 2026-07-02 | v1 foundation | Accepted |
| [0002](#adr-0002--production-hosting) | 2026-07-25 | Production hosting | Accepted |
| [0003](#adr-0003--personal-access-tokens) | 2026-07-28 | Personal access tokens | Accepted |
| [0004](#adr-0004--grade-aware-intervals) | 2026-09-06 | Grade-aware intervals | Accepted |
| [0005](#adr-0005--reset-progress-and-reviewed-today) | 2026-09-06 | Reset progress and reviewed-today | Accepted |
| [0006](#adr-0006--translate-mode) | 2026-11-16 | Translate mode | Accepted |
| [0007](#adr-0007--citation-form-and-article-insensitive-matching) | 2026-11-16 | Citation form and article-insensitive matching | Accepted |
| [0008](#adr-0008--newest-first-new-cards-and-study-again) | 2026-11-16 | Newest-first new cards and Study again | Accepted |
| [0009](#adr-0009--installable-app-without-a-service-worker) | 2026-11-16 | Installable app without a service worker | Accepted |

---

## ADR-0001 — v1 foundation
- **Status:** Accepted (2026-07-02).
- **Context:** the DanishLearner POC (Razor Pages, SQLite, single user) proved the idea. A hosted, multi-user, language-generic rewrite was needed.
- **Decision:** the 17 locked decisions in `CLAUDE.md` → "Confirmed decisions":
  - multi-user accounts
  - 1 learning language + N known languages
  - `.apkg` import with progress
  - Claude Haiku lookup behind `IWordLookupProvider`, with a server-level key
  - sanitized HTML content
  - tags + study-all
  - SM-2
  - .NET 10 Web API + EF Core + PostgreSQL
  - Angular + Material
  - Docker
  - minimal auth
- **Consequences:**
  - The owner is a .NET developer new to Angular, so frontend code must stay plain and commented.
  - The DB is the source of truth; Anki is only an import target.
- **Links:** `docs/SPECIFICATION.md`, `docs/IMPLEMENTATION_PLAN.md`.

## ADR-0002 — Production hosting
- **Status:** Accepted (2026-07-25). Resolves `CLAUDE.md` decision #13 ("CI/CD + reverse proxy deferred").
- **Context:** v1 had to run on the owner's shared VPS next to another application.
- **Decision:**
  - **One image.** The Angular build is baked into the API's `wwwroot`, built by GitHub Actions and pushed to GHCR.
  - **Deploy.** CD deploys over SSH to a forced-command script that runs `docker compose pull/up` under a rootless Docker user and waits for `/api/health`.
  - **Proxy.** Host nginx is a pure reverse proxy; TLS uses a Cloudflare Origin CA certificate, with Cloudflare in front.
  - **Backups.** restic backups.
- **Consequences:**
  - Static files are served by ASP.NET, so their cache headers are the app's job (see ADR-0009).
  - Host bootstrap lives in the private ops repo.
- **Links:** `infra/README.md`.

## ADR-0003 — Personal access tokens
- **Status:** Accepted (2026-07-28).
- **Context:** scripts and Claude skills (e.g. `danish-word`) need to add words without a browser session.
- **Decision:**
  - Long-lived `ankl_…` tokens as a second auth scheme, routed by a policy scheme next to the SPA JWT.
  - Each token has full-account scope and is managed only by a password login.
  - No scopes, no management UI.
- **Consequences:** a leaked token is a full-account credential; keep tokens out of shared scripts.
- **Links:** `README.md` → API tokens.

## ADR-0004 — Grade-aware intervals
- **Status:** Accepted (2026-09-06).
- **Context:** textbook SM-2 gives Hard, Good and Easy the same next interval, so the three button hints read alike.
- **Decision:**
  - Keep the SM-2 ladder as the Good baseline.
  - Layer on Anki's hard factor (1.2), easy bonus (1.3) and easy interval (4 d).
  - Cap the interval with the per-user `MaxIntervalDays` (default 36500).
- **Consequences:** intervals no longer match textbook SM-2 exactly; the rules are in spec §5.
- **Links:** `Core/Srs/Sm2.cs`, spec §5.

## ADR-0005 — Reset progress and reviewed-today
- **Status:** Accepted (2026-09-06).
- **Context:** the user wanted to restart a badly learned word, and to see how much had been studied today.
- **Decision:**
  - `DELETE /api/words/{id}/progress` drops the word's SRS states in every direction, behind a confirm dialog.
  - `reviewedToday` is derived from `SrsState.LastReviewed` against the UTC day.
  - No schema change.
- **Consequences:**
  - A reset word becomes "new" again.
  - Since ADR-0008 (newest-first) a reset *old* word waits behind newer new words; that's why "Study again" was added.
- **Links:** spec FR-D8, FR-R1.

## ADR-0006 — Translate mode
- **Status:** Accepted (2026-11-16).
- **Context:**
  - The owner mostly meets unknown Danish words on an Android phone.
  - The AI lookup only worked Danish → known languages, and was buried in the add-word form.
- **Decision:**
  - **Two modes.** `/` is a neutral selector: Translate or Learn. The owner didn't know which mode he'd use more. The study dashboard moved to `/study`.
  - **`POST /api/translate`** runs on the same Claude provider as lookup:
    - **Source detection:** auto-detects the source language among the user's languages, with a manual override; any other language returns 422.
    - **Input limits:** at most 300 characters, i.e. one sentence.
    - **Cost control:** shares the per-user `lookup` rate limit.
    - **Schema:** the structured-output schema is identical for every request of a user, because the API caches compiled schemas. The override travels in the user message.
  - **Result.** The translation plus **AI-picked learning-language cards**:
    - **Danish input:** the word's entry.
    - **English input:** Danish alternatives, with a note on how they differ.
    - **Sentence:** the words worth learning.
    - Multi-word input also gets an optional "whole input" card.
    - When the input is a sentence, every card's example is the user's own sentence.
  - **One-tap Add.** No review form; saved words are tagged `translated`. Words already in the dictionary are flagged and offer Study again (ADR-0008) or Add anyway.
  - **Back without a new call.** The last result is kept in memory (a root state service keyed by user + languages), so Back doesn't make a new paid call. This is not history.
  - **Rules live in Core.** All deterministic rules are in a pure, tested `TranslateShaper`. The controller only validates, calls the provider and flags existing words.
- **Alternatives rejected:**
  - A translate box on the home page: zero taps, but the owner preferred a neutral chooser.
  - Picking words by tapping the sentence: too much phone UI.
  - A review form before saving: slower; edits can happen later.
- **Consequences:**
  - Every translation is a paid AI call, at about 2–12 s on Haiku 4.5 (not yet measured live).
  - AI mistakes can land in the dictionary, but they stay editable.
  - No schema change or migration.
- **Links:**
  - `docs/TRANSLATE_PLAN.md`: design record, review deviations and phases.
  - Spec §3.8 (FR-T*).

## ADR-0007 — Citation form and article-insensitive matching
- **Status:** Accepted (2026-11-16).
- **Context:**
  - The owner's dictionary mixes forms: ~290 nouns are stored as "en aftale", 39 verbs as "at stege", the rest bare.
  - The study card doesn't show the separate gender field.
  - Exact matching missed about a third of existing words.
- **Decision:**
  - **Saved term = citation form.** AI-produced terms (Translate and the form's Look up) carry the article or infinitive marker ("en aftale", "at spise") for languages with a particle table (only Danish today); other languages use the lemma. The gender field is still filled.
  - **Matching ignores one leading particle.** `TermNormalizer.MatchKey/MatchCandidates` strip one particle from the per-language table (`da` → en/et/at). Candidates feed an indexed `TermNormalized IN (…)` query. An exact match wins, then the oldest word, then the lowest id. Used by the duplicate warning and Translate.
  - **Homographs stay separate.** A saved word that exactly matches one card never flags a different card ("gift" vs "en gift"). Same-spelling cards merge their meanings.
- **Consequences:**
  - Accepted false positives such as "en gang" vs "gang" (hence "Add anyway").
  - The Anki importer still matches exactly (`BACKLOG.md`).
  - Other learning languages need a table entry to get the same behaviour.

## ADR-0008 — Newest-first new cards and Study again
- **Status:** Accepted (2026-11-16).
- **Context:** with ~460 never-studied imported card directions and 20 new cards a day, a word met today would wait about three weeks under oldest-first ordering.
- **Decision:**
  - **Newest-first.** New cards are served newest-first (`CreatedAt` desc, `Id` as a stable tie-break).
  - **Study again** (`POST /api/words/{id}/relearn`, `Sm2.Relearn`) instead of reset:
    - Repetitions 0, interval 0, lapses + 1, ease kept.
    - `Due = min(Due, now)`, so an overdue card keeps its place.
    - Never-studied directions get a state that is due now; it counts toward today's new allowance.
    - It deletes nothing, so there is no confirm dialog.
    - Offered on Translate cards and in the word form, next to Reset progress.
- **Consequences:**
  - The imported backlog only gets the daily slots left over after new words.
  - Reset progress now sends an old word behind the backlog; Study again is the intended "I forgot this" action.

## ADR-0009 — Installable app without a service worker
- **Status:** Accepted (2026-11-16).
- **Context:**
  - Opening the site quickly on the phone matters for Translate mode.
  - Android can share selected text to an installed web app.
- **Decision:**
  - **Manifest:** `manifest.webmanifest` gives a standalone app, original icons, long-press shortcuts (Translate, Learn) and a GET **share target** → `/translate?q=…` (also reading `title`/`url`).
  - **No service worker.** Chrome no longer requires one to install, and one risks serving stale deploys.
  - **Cache policy (fixed with this change):**
    - Only content-hashed `.js`/`.css` are `immutable` for a year.
    - Everything else, including `index.html`, the SPA fallback for deep links, the manifest, icons and favicon, is `no-cache`.
    - One shared `StaticFileOptions` (`SpaStaticFiles`) for both.
  - **Login keeps the target.** Login preserves a validated `returnUrl` (`safeReturnUrl`), so a share survives re-login.
- **Consequences:**
  - No offline mode.
  - An installed app resumed after a deploy can hit a missing lazy chunk (`BACKLOG.md`).
  - Very long GET shares get a 414.
  - The first deploy needs a one-time Cloudflare purge, because the old favicon was cached as immutable.
- **Links:** `infra/README.md` → Android app (PWA), smoke test.
