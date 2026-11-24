# Architecture Decision Records

> One record per significant decision or feature: the context, what was chosen, and what it costs.
> Lives in `docs/decisions/` next to the long-form design records (see its README).
> Append new records at the bottom with the next number. Never rewrite an accepted record. Supersede
> it with a new one and set the old one's status to "Superseded by ADR-NNNN". Defects to fix later go
> in [`BACKLOG.md`](../BACKLOG.md), not here. Records 0001–0005 were written retroactively (2026-11-16)
> from the project history.

| # | Date | Title | Status |
|---|------|-------|--------|
| [0001](#adr-0001--v1-foundation) | 2026-07-02 | v1 foundation | Accepted |
| [0002](#adr-0002--production-hosting) | 2026-07-25 | Production hosting | Accepted |
| [0003](#adr-0003--personal-access-tokens) | 2026-07-28 | Personal access tokens | Accepted |
| [0004](#adr-0004--grade-aware-intervals) | 2026-09-06 | Grade-aware intervals | Superseded by 0013 |
| [0005](#adr-0005--reset-progress-and-reviewed-today) | 2026-09-06 | Reset progress and reviewed-today | Accepted |
| [0006](#adr-0006--translate-mode) | 2026-11-16 | Translate mode | Accepted |
| [0007](#adr-0007--citation-form-and-article-insensitive-matching) | 2026-11-16 | Citation form and article-insensitive matching | Accepted |
| [0008](#adr-0008--newest-first-new-cards-and-study-again) | 2026-11-16 | Newest-first new cards and Study again | Accepted |
| [0009](#adr-0009--installable-app-without-a-service-worker) | 2026-11-16 | Installable app without a service worker | Accepted |
| [0010](#adr-0010--claude-haiku-55-model-and-effort-in-config) | 2026-11-16 | Claude Haiku 5.5; model and effort in config | Accepted |
| [0011](#adr-0011--appenv-edits-apply-without-a-redeploy) | 2026-11-16 | `app.env` edits apply without a redeploy | Accepted |
| [0012](#adr-0012--review-log) | 2026-11-24 | Review log | Accepted |
| [0013](#adr-0013--fsrs-6-scheduler) | 2026-11-24 | FSRS-6 scheduler | Accepted |
| [0014](#adr-0014--one-learning-step-for-new-cards) | 2026-11-24 | One learning step for new cards | Accepted |

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
- **Status:** Superseded by ADR-0013 (2026-11-24). Accepted 2026-09-06.
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
  - `docs/decisions/translate-mode.md`: design record, review deviations and phases.
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

## ADR-0010 — Claude Haiku 5.5; model and effort in config
- **Status:** Accepted (2026-11-16).
- **Context:**
  - `claude-haiku-5-5` costs about a tenth of `claude-haiku-4-5` ($0.10 / $0.50 vs $1 / $5 per million input / output tokens).
  - It thinks by default (adaptive thinking, default effort `medium`), which adds latency and output tokens. `claude-haiku-4-5` rejects the effort parameter.
  - The model id was a code default, so every new model needed a code change.
- **Decision:**
  - `Anthropic:Model` defaults to `claude-haiku-5-5` and `Anthropic:Effort` to `low`. Both apply to word lookup and Translate, which share one request path (`ClaudeLookupProvider.CreateAsync`).
  - Effort goes out as `output_config.effort`. `default` (or empty) sends none, so a model without effort support still works.
  - Thinking stays on; effort is the only knob. Thinking blocks come back empty and are skipped; only the text block is parsed.
  - `MaxTokens` (4096) now includes thinking. `TimeoutSeconds` (30) is unchanged.
  - Production sets `ANTHROPIC_MODEL` / `ANTHROPIC_EFFORT` in `app.env`; locally the key lives in user-secrets (`UserSecretsId` on the API project).
- **Consequences:**
  - Rolling back is config only: `Anthropic:Model=claude-haiku-4-5`, `Anthropic:Effort=default`.
  - Latency against Haiku 4.5 is not measured yet. If translations hit the 30 s timeout, lower the effort or raise `TimeoutSeconds`.
  - A refusal still ends as a 502 (`EnsureCompleted`); Haiku 5.5 has no server-side refusal fallback.

## ADR-0011 — `app.env` edits apply without a redeploy
- **Status:** Accepted (2026-11-16).
- **Context:**
  - `anki-deploy` compared only image IDs. An edit to `app.env` (a new model, effort or key) sat unapplied until the next image shipped, and the 5-minute timer did nothing either.
  - With the model now in config (ADR-0010), changing a setting should not need a code push.
- **Decision:**
  - `anki-deploy` also compares `docker compose config --hash` (computed from the files as they are now) with the config-hash label on the running containers. Any difference runs `up -d`, which recreates only the changed services.
  - `app.env` is the one production settings file; `app.env.example` lists the AI settings first.
  - The API still gets only the variables compose maps for it, not the whole file: `app.env` holds the Postgres superuser password, which the API never sees.
- **Consequences:**
  - An `app.env` edit takes effect within 5 minutes, or at once with `sudo machinectl shell anki@ /usr/local/bin/anki-deploy`.
  - The script is installed on the host by hand, not shipped by CD. It takes effect only after it is reinstalled there (`infra/README.md`).

## ADR-0012 — Review log
- **Status:** Accepted (2026-11-24).
- **Context:**
  - `SrsState` keeps only the latest snapshot per card, so every grade overwrites the one before. Nothing records *when* a card was reviewed or *which* button was pressed.
  - The planned FSRS scheduler is fitted to a user's own review history and can replay it. Every day without a log is history lost for good; July–November 2026 already is.
- **Decision:**
  - New append-only table `ReviewLog` (`ReviewLogEntry`): one row per event, per direction.
    - `Kind`: `Review` (a grade press, in-session Again repeats included), `Relearn` (Study again) or `Reset`. Relearn and reset are their own kinds so a later fit or replay doesn't mistake them for grades.
    - `Source`: `Site`, or `Anki` for a future import of Anki's revlog into the same table.
    - The grade, the event time, `PreviousReviewAt` (real elapsed time, overdue days included), and interval / ease / due before and after.
  - The row is written in the same transaction as the state change. Reset inserts its rows and runs `ExecuteDelete` inside one explicit transaction, so a concurrent reset still deletes nothing instead of failing.
  - Deleting a word deletes its history (cascade); resetting it keeps the history plus a `Reset` row.
  - No API or UI yet; the data is read with SQL until FSRS needs it.
- **Consequences:**
  - About one row per press: a few hundred rows a day at most.
  - The state before the first logged event of a pre-existing card is in that row's `…Before` columns. Earlier history exists only in Anki's export.
  - `reviewedToday` (FR-R1) still counts from `SrsState.LastReviewed`; it could move to the log later.
  - FSRS will add its own columns (stability, difficulty, retrievability) in its migration.

## ADR-0013 — FSRS-6 scheduler
- **Status:** Accepted (2026-11-24). Supersedes ADR-0004 and the SM-2 part of ADR-0001's decision #8.
- **Context:**
  - SM-2 as built here penalised ease −0.32 per Again press (in-session repeats included) and only Easy restored it, so struggling cards sank to the 1.3 floor for good ("ease hell").
  - It ignored overdue time and restarted the 1 d / 6 d ladder after any lapse. That is costly for a learner who skips sessions.
  - FSRS (Anki's scheduler since 23.10) models recall per card, predicts it markedly better, and can be fitted to the user's own history. On the primary user's Anki history, fitted weights roughly halved the calibration error of the defaults.
- **Decision:**
  - **Engine:** FSRS-6 (21 weights) ported from py-fsrs 6.3.3 into `Core/Srs/Fsrs.cs`. Tests pin values from a calendar-day replay that reproduced Anki's own `compute_memory_state` on a real 1.4k-card collection, plus one real card checked against Anki directly.
  - **Deviations from py-fsrs, all Anki's behaviour:**
    - elapsed time in calendar days, cut at UTC midnight (Anki: 04:00 local);
    - passing intervals kept in order (Good ≥ Hard + 1, Easy ≥ Good + 1);
    - no learning steps (Again = 10 minutes);
    - deterministic fuzz, so hints equal what a grade schedules.
  - **Settings:** desired retention (default 0.90) and pasted weights (default py-fsrs's). Fitting stays outside the app: Anki's optimizer, which also runs headless through the `anki` pip package.
  - **Setup of pre-FSRS cards ("hybrid"):**
    - Difficulty from the replayed Anki history, Stability from it where the card wasn't reviewed on the site since the import, else from the SM-2 snapshot.
    - Without history, a lapse-count heuristic for Difficulty. Converting SM-2 ease was measured and rejected: with fitted weights it put nearly every card at D ≈ 8 where history said ≈ 1.
    - Every set-up card is rescheduled from its memory.
  - **Re-running setup:** setup reruns for cards not yet studied under FSRS after startup, after an import and after a change of weights / retention / cap. `Setup` log rows keep the SM-2 starting point, and an unchanged result writes nothing.
  - **Anki history import (FR-I9):** the `revlog` goes into the review log. A "history only" mode adds no words, so deleted words don't come back. One card per word and direction, picked by the fields FSRS never changes; idempotent.
  - **Study order:** due Again repeats → new words, while a daily limit allows → due reviews, highest recall first → new words when unlimited → learn-ahead.
  - **Concurrency:** `SrsState` gets an `xmin` row version. A setup run skips cards changed under it, and a colliding grade or Study again gets 409.
- **Consequences:**
  - Intervals get much longer for well-known cards, and a stale backlog shrinks at setup: cards FSRS considers still known leave it.
  - Study again keeps its meaning (forgotten, no difficulty penalty), but now also counts as reviewed today (FR-R1), because it sets `LastReviewed`.
  - `EaseFactor` / `Repetitions` are legacy inputs; dropping them is a backlog item.
  - Later re-fits apply to already studied cards going forward only. A full recompute from the log is a backlog item, and so is a per-user day start.
- **Links:** spec §5, FR-S8, FR-I9, FR-R4; `docs/decisions/fsrs.md`; `Core/Srs/Fsrs.cs`, `Core/Srs/FsrsSetup.cs`, `Api/Services/FsrsInitializer.cs`.

## ADR-0014 — One learning step for new cards
- **Status:** Accepted (2026-11-24). Supersedes ADR-0013's "no learning steps".
- **Context:**
  - Without learning steps, a new card's first Good scheduled days at once. With weights fitted to the primary user's Anki history, that was about two weeks for a word seen for the first time, with no repeat in the session (1–2 days with the default weights).
  - Those weights were fitted on Anki reviews where new cards also got same-day learning-step repeats (Anki's default steps are 1 and 10 minutes).
- **Decision:**
  - A new card's first Hard or Good brings it back in 10 minutes (`Fsrs.LearningStepMinutes`), with its memory already started from that grade. The same-day review after it uses FSRS's short-term formula and schedules days.
  - A new card's first Easy still schedules days. Again is 10 minutes as before.
  - One step, not Anki's two: a new word is seen at least twice in its first session either way, because a first Again also brings it back in 10 minutes.
- **Consequences:**
  - About one extra review per new word (at most the daily new limit per day). In exchange, a new word's first day interval rests on a same-day recall.
  - When nothing else is left, learn-ahead (FR-R9, 20 minutes, as Anki's default) serves the repeat early, possibly right away. That matches Anki, and a session can always be finished.
  - New-card hints read "10 min / 10 min / 10 min / N d".
  - The repeat is an interval-0 card, so it is served before new words like any Again repeat (FR-R4).
- **Links:** spec §5.2–5.3; `Core/Srs/Fsrs.cs`.
