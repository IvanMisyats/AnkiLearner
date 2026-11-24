# FSRS scheduler — plan

> Working plan for replacing SM-2 with FSRS-6. Source of truth while the feature is built; update
> the status at the end of every phase. Status: **built, waiting for merge and deploy** (2026-11-24).
> The repo is public: personal numbers (fitted weights, account counts) are kept out of this file.

## Status

- [x] Phase 1 — Review log (FR-R11, ADR-0012). Committed on `main`; live once pushed.
- [x] Phase 2 — FSRS engine (Core, pure): `Fsrs.cs`, reference vectors + a real Anki card, two review rounds
- [x] Phase 3 — Schema + settings backend (migration `Fsrs`; retention default 0.9 for existing rows; unique Anki revlog index)
- [x] Phase 4 — Switch scheduling to FSRS (memory-state setup, Study again, study order; xmin row version; two review rounds)
- [x] Phase 5 — Anki history import (revlog parser hardened for untrusted input; "history only" mode)
- [x] Phase 6 — Frontend (settings: retention + weights, sent only when changed; import: history option + History only)
- [x] Phase 7 — Docs (spec §5 rewritten, FR-S8, FR-I9, ADR-0013), full suites, end-to-end check

## Deviations from the plan (recorded while building)

- **Setup log rows.** Setup rewrites a card's interval, due and last review, so a later re-setup
  needs the original SM-2 values: each setup writes a `Setup` review-log row, and the first one's
  "before" columns are the starting point. An unchanged result writes nothing.
- **Optimistic concurrency.** A setup run could overwrite a card graded meanwhile: `SrsState` got
  an `xmin` row version (no real column). Colliding grades / Study again get 409.
- **After-startup run redoes setup for every user** (not only states without memory), so a re-setup
  that failed after a settings change is retried at the next start. Cheap: unchanged = no write.
- **Study order with no daily limit:** new words come after the due reviews (new-first only with a
  limit), and due Again repeats always come first.
- **Interval hints formatted on the server** ("12 d", "3.5 mo", "1.3 y"), not in the frontend.
- **Lapses** count only for a card that had a day interval (Again on a new card or on a repeat, and
  Study again on a repeat, no longer count). This also fixed two backlog items.
- **History import** gained hardening from two review rounds: id range + dedupe, Anki Forget
  (verified against Anki 26.09), a 500k-row cap, atomic claim of an upload.

## Findings (2026-11-24)

- **No per-review history before Phase 1.** `SrsState` is the latest SM-2 snapshot; imported cards
  never reviewed on the site have `LastReviewed = null`. In the primary instance, most imported
  cards were never reviewed on the site, so for them the Anki revlog is the only real history.
- **Reference:** py-fsrs 6.3.3 = **FSRS-6, 21 weights**. Good leaves Difficulty unchanged except
  for mean reversion (`w7`).
- **Elapsed days are calendar days**, as Anki counts them. A replay counting calendar days matched
  Anki's own `compute_memory_state` exactly (S and D) on a real 1.4k-card collection; py-fsrs counts
  24 h periods and lands ~5% low on S. Weights fitted by Anki's optimizer assume calendar days.
- **Weights fitted to the user's own history** beat the defaults clearly (in-sample log loss and
  calibration both much better). Anki's optimizer runs headless via the `anki` pip package
  (`compute_fsrs_params`), so no Anki UI is needed.
- **SM-2 → FSRS conversion of ease is unusable** with fitted weights (converted D ≈ 8 where replay
  gives ≈ 1). A **lapse heuristic** — D0(Good), one Again-update per lapse, up to three
  Good-updates — matches replay D (median error 0, |err| > 2 on ~6% of cards), also when every
  Again press counts as a lapse, as the site's `Lapses` does.
- **Rescheduling from history shrinks a stale backlog a lot**: with fitted weights, a large share of
  long-overdue cards still has recall above the threshold. How many stay due depends strongly on the
  desired retention.

## Decisions

- 2026-11-24 (Ivan): **hybrid** memory-state setup — Anki history where it exists, the site
  snapshot where it is newer.
- 2026-11-24: the review log ships first, alone (done).
- 2026-11-24 (Ivan, plan approved): **retention 0.85** for his account (default stays 0.90);
  **reschedule at setup**: yes; **study order**: new words first (within the daily limit), then due
  cards highest-recall first. The options are kept under "Decisions for Ivan" for the record.

## Design

### Engine — `AnkiLearner.Core/Srs/Fsrs.cs` (pure, no EF)
- FSRS-6, 21 weights; defaults = py-fsrs 6.3.3 `DEFAULT_PARAMETERS`; bounds = its
  `LOWER/UPPER_BOUNDS_PARAMETERS` (used to validate pasted weights).
- `FsrsParameters(double[] Weights, double DesiredRetention, int MaxIntervalDays)`.
- Formulas exactly as py-fsrs `scheduler.py`: initial S/D, retrievability, next interval,
  short-term S (Good/Easy factor clamped ≥ 1), recall S with hard penalty / easy bonus, forget S
  capped by `S / e^(w17·w18)`, D with linear damping + mean reversion to unclamped D0(Easy).
- **Deviation from py-fsrs, matching Anki:** elapsed days = UTC calendar-day difference
  (`now.Date − lastReviewed.Date`); 0 → same-day short-term formula.
- `Apply(state, grade, now, p)`:
  - no memory state → initial S/D from the grade;
  - otherwise short-term or long-term S update, then D update;
  - Again → due in **10 min** (in-session repeat, unchanged), `IntervalDays = 0`, `Lapses + 1`;
  - Hard/Good/Easy → `IntervalDays = fuzz(nextInterval(S'))`, due = now + that;
  - returns R at review time, for the log.
- **Fuzz** as py-fsrs (`FUZZ_RANGES`, intervals ≥ 2.5 d) but **deterministic**, seeded from
  (WordId, Exercise, previous LastReviewed), so the button hint equals what Apply schedules.
  Spreads the pile of cards reviewed in one catch-up session.
- `PreviewDays(state, grade, p)` for button hints (Apply on a copy).
- `SrsState.EaseFactor` / `Repetitions` stop changing (legacy SM-2, dropped later). `Sm2.cs` and
  its tests are deleted.

### Memory-state setup — `FsrsInitializer` (Api/Services)
Gives each state that has **no FSRS-era review yet** a memory state (S, D, last review) and
**reschedules** it: `IntervalDays = fuzz(nextInterval(S))`, `Due = last + IntervalDays` (still due
if that is in the past). "No FSRS-era review" = no review-log row with a stability value for that
card, so such a card can be set up again safely; a reviewed card is never touched.

Which Anki rows count for a card: `Source = Anki`, newer than the state's `CreatedAt`, and newer
than the card's latest `Reset` row. So a word reset or re-added since the import starts fresh.

| case | D | S and last review |
|---|---|---|
| Anki history, not reviewed on the site (`LastReviewed` null) | replay | replay (exact) |
| Anki history, reviewed on the site | replay, + one Again-update per site lapse | snapshot: S = max(`IntervalDays`, w0), last = `LastReviewed` |
| no Anki history (site words, SM-2 imports without history) | lapse heuristic | snapshot, last = `LastReviewed` ?? `Due − IntervalDays` |

- Site lapses = `Lapses` − Anki review lapses (Anki Again rows with `IntervalBefore` > 0), floor 0.
- Snapshot S under-rates cards whose site reviews went well. That is deliberate: the first FSRS
  review sees a lower predicted R and boosts S accordingly, at the cost of one early review.

Triggers (each runs per user, in its own transaction, logs and swallows its own errors):
1. **After startup**, as a hosted background service: every state with `Stability` null. Never on
   the startup path itself, so a failure cannot crash-loop the API. Idempotent: a no-op once done.
2. **After an Anki history import** (Phase 5), for the importing user.
3. **After the user saves different FSRS weights**, for that user.

So "paste weights" and "import history" can happen in either order, as long as both happen before
the cards are studied. Later re-fits apply to already reviewed cards going forward only (a full
recompute is a backlog item).

### Data model
- `SrsState`: `Stability double?`, `Difficulty double?` (null = not set up / new).
- `ReviewLogEntry`: `StabilityBefore/After`, `DifficultyBefore/After`, `Retrievability` (R at the
  event), `AnkiRevlogId long?` (imported rows).
- `UserSettings`: `DesiredRetention double` (default **0.90**, allowed 0.70–0.97),
  `FsrsWeights double[]?` (Postgres `double precision[]`; null = defaults; exactly 21 values within
  bounds).

### Study again (FR-D8) under FSRS
Keeps its meaning — "I forgot this, without a difficulty penalty": S → forget stability at the
current R, **D unchanged**, `Lapses + 1`, due now (an overdue card keeps its earlier due),
`LastReviewed = now` so the in-session review after it uses the same-day formula. Side effect: a
relearned word counts in today's "reviewed" number (FR-R1). Logged as `Relearn` with S/D.

### Study order (FR-R4) — see Decisions for Ivan
Proposed: due cards by **highest retrievability first** (R desc ⇔ elapsed / S asc; computed in
memory over the due set), and **new words before due reviews**, within the daily new limit.

### Anki history import (Phase 5)
- Parser reads `revlog` (`id, cid, ease, ivl, lastIvl, factor, type`) for the package's cards;
  keeps types 0–3 with ease 1–4.
- A commit mode **"History only"**: adds **no** words (a re-upload must not resurrect words deleted
  since the first import); attaches history to existing words. A normal import can also carry
  history for the words it adds.
- Matching: the importer's normalized-term duplicate check (exact `TermNormalized`) + direction
  from card `ord` (0 → TargetToKnown, 1 → KnownToTarget). Only cards whose (word, direction) has a
  state get history.
- **One Anki card per (word, direction).** When several notes map to one word, take the card whose
  `ivl` / `factor` equal the state's `IntervalDays` / `EaseFactor` (an unchanged import); otherwise
  skip — the card falls back to the snapshot row.
- A card that already has Anki rows is skipped (idempotent re-upload). Afterwards the initializer
  runs for the user. The preview shows how many existing words would get history.
- Untrusted input: same limits and SQLite handling as the existing parser.

### Frontend (Phase 6)
- Settings: **Desired retention** (%, 70–97) with one line of explanation; **FSRS weights** textarea
  ("paste from Anki → Deck options → FSRS"), blank = defaults, server-validated.
- Import: "History only" option + the preview count.
- Interval hints: long intervals read as months/years ("8 mo", "1.3 y") instead of "465 d".

## Phases (each: build → tests → code review → green → commit on the branch)

One branch `feature/fsrs`, squash-merged as **one commit** at the end (one push, one deploy).

| # | Phase | Review depth |
|---|---|---|
| 2 | **Engine**: `Fsrs.cs` + unit tests. Expected values from the calendar-day replay (verified = Anki) on synthetic sequences: each first grade, same-day repeats, long gaps, lapses, Hard/Easy, max-interval cap, bounds; fuzz range + determinism; preview = apply. | deep (2 rounds) |
| 3 | **Schema + settings**: migration (SrsState S/D; log S/D/R + `AnkiRevlogId`; retention + weights), settings API + validation, tests. | one |
| 4 | **Switch**: `StudyController` on `Fsrs` (grade, hints, log S/D/R), `FsrsInitializer` + hosted startup run + weights-change trigger, Study again, study order, importer states → initializer; delete `Sm2`; update study/import tests. | deep (2 rounds) |
| 5 | **History import**: parser revlog, matching + one-card rule + CreatedAt/Reset filter, history-only mode, preview count, idempotency, initializer trigger; parser + integration tests. | deep (untrusted input) |
| 6 | **Frontend**: settings fields, import option, interval formatting; unit tests. | one |
| 7 | **Docs + verify**: spec §5 rewritten for FSRS, FR updates, ADR-0013 (FSRS; supersedes the SM-2 parts of ADR-0001 #8 and ADR-0004), CLAUDE.md, README; full suites; end-to-end locally with a real `.apkg` in a test account (history-only import, pasted weights, counts, hints, backlog shrink). | — |

## After deploy — the user's steps, before studying
1. Settings → paste the fitted weights and set the desired retention.
2. Import → upload the Anki `.apkg` with **History only**.
3. Study; check the hints look sane.

## Pre-mortem
- **Port drift from Anki** → tests pin values from the reference replay; one more test on a real
  card's sequence (grades + day offsets only, no words).
- **Startup setup fails in production** → hosted service with catch-and-log, per-user
  transactions. Study never breaks: a state still without a memory state is set up on the spot when
  it is graded or previewed (same per-card rule, Anki rows included).
- **Studying between deploy and history import** sets those cards up for good → steps 1–2 right
  after deploy; worst case only cards studied in between miss their history.
- **Long-gap extrapolation** may be optimistic → the review log will show real retention; retention
  is one setting away.
- **Term edited since import** → no history match → snapshot row.
- **Rescheduling at setup pushes many cards months out** → intended; that is the efficiency gain.

## Backlog (deliberately not in this feature)
- Recompute all memory states from the log when weights change (Anki's "reschedule on change").
- Server-side optimizer, or a review-log export in Anki-optimizer format for re-fitting.
- Use FSRS memory state from `cards.data` when importing from an FSRS-enabled Anki.
- Drop the legacy `EaseFactor` / `Repetitions` columns.

## Decisions for Ivan
1. **Desired retention** (Settings, changeable any time; the default for new users stays 0.90).
   Lower = fewer reviews, more forgetting; on a stale backlog the due count roughly halves from
   0.90 to 0.85. Recommendation: **0.85** for a learner who skips sessions.
2. **Reschedule at setup** — recompute every card's due date from its FSRS state (cards FSRS thinks
   you still know leave the backlog). Recommendation: **yes**; without it the stale SM-2 due dates
   keep the whole backlog due.
3. **Study order with a backlog.** Today: due cards oldest-due first, new words only after every due
   card. Proposed: (a) **new words first**, within the daily new limit, so words added via Translate
   are studied the same day instead of waiting behind hundreds of reviews; (b) due cards **highest
   recall first** — cheap reviews of cards that just crossed the threshold before long-forgotten
   ones. Cost of (b): while a backlog persists, the most-forgotten cards keep waiting.
   Recommendation: **both (a) and (b)**.
