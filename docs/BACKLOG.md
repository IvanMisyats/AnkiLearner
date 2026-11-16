# Backlog — known defects and follow-ups

> Defects discovered during work (reviews, end-to-end checks) that were deliberately **not** fixed
> in the change that found them. Add new items at the top of their section. Remove an item in the
> commit that fixes it. This repo is public: never log details of an unfixed security
> vulnerability here (keep those private until the fix ships).

## Defects

### Installed app breaks on navigation after a deploy (ChunkLoadError)
- **Found:** 2026-11-16, Translate mode Phase 4 review. This defect existed before; installing the app makes it more likely.
- **Symptom:**
  - Android keeps the installed app (PWA) in memory and resumes it after a new version is deployed. The resumed page still runs the old JS bundle.
  - Tapping into a lazy-loaded page it hasn't opened yet (e.g. Dictionary, Settings) requests an old `chunk-*.js` that no longer exists. The server answers with `index.html` (the SPA fallback), so the import fails with a ChunkLoadError.
  - **The tap does nothing until the app is reloaded.**
  - Full navigations (share target, shortcuts, a cold start) are fine.
- **Fix idea:** reload the page once when a lazy route fails to load, e.g. `withNavigationErrorHandler(...)` in `app.config.ts`. On a chunk-load error, do `location.assign(targetUrl)`, with a session-storage guard against reload loops. About 10 lines plus a spec.

### Import duplicate detection is still exact-match
- **Found:** 2026-11-15, Translate mode Phase 1a.
- **Symptom:** the Anki importer compares `TermNormalized` exactly. Importing a deck with "aftale" when the dictionary has "en aftale" (or the reverse) creates a duplicate. The add-word form and Translate already ignore a leading article.
- **Fix idea:** use `TermNormalizer.MatchCandidates` in `ImportController`'s duplicate check, preferring an exact match like `WordsController.CheckDuplicate`.

### Two simultaneous first reviews of a direction return 500
- **Found:** 2026-11-15, Translate mode Phase 1a review.
- **Symptom:** a relearn racing another relearn or a first grade on a never-studied direction can return a 500. So can two grades of one new card, or a relearn racing a reset. The unique index on `(WordId, Exercise)` rejects the second insert. The final state is correct. The UI disables its buttons while a request runs, so this needs two tabs or devices.
- **Fix idea:** catch the unique-violation `DbUpdateException` in `StudyController.Grade` / `WordsController.Relearn` and retry once (reload the state and apply), or return 409.

### Word-form Notes field shows raw HTML
- **Found:** 2026-11-16, Translate mode end-to-end check.
- **Symptom:** Notes is an HTML field. An AI note saved from Translate ("Hund & kat", "<1 year") shows as `Hund &amp; kat` / `&lt;1 year` in the word form's Notes textarea, and its label doesn't say "HTML allowed" like the other HTML fields. It displays fine everywhere else.
- **Fix idea:** at least label it "Notes (HTML allowed)" like the other fields. Better, a small rich-text or plain-text-with-escaping editor for all HTML fields.

### Very long shared text fails with a raw 414 page
- **Found:** 2026-11-16, Translate mode Phase 4 review.
- **Symptom:** the share target uses GET, so the shared text goes in the URL. More than about 8 KB once encoded (≈1,300 Cyrillic or ≈7,000 Latin characters) is rejected by nginx/Kestrel with a bare 414 before the app loads. Translate only uses 300 characters anyway.
- **Fix idea:** accepted for now. A POST share target would need a server endpoint that redirects to `/translate?q=<first 300 chars>`.

### Toolbar overflows below 320 px
- **Found:** 2026-11-16, Translate mode Phase 3.
- **Symptom:** screens narrower than 320 CSS px (e.g. a folded Galaxy Fold at 280 px) scroll sideways by about 16 px. 320 px and up is fine.
- **Fix idea:** under 320 px, shrink the nav gaps or hide Settings behind a menu.

### Lapses counted twice for one forgetting
- **Found:** 2026-11-15, Translate mode Phase 1a review.
- **Symptom:** grading Again and then pressing Study again (or double-tapping Study again) adds two lapses. Cosmetic: `Lapses` doesn't affect scheduling and isn't shown.
- **Fix idea:** skip `Lapses += 1` in `Sm2.Relearn` when the state is already due now with 0 repetitions.

## Follow-ups (improvements, not defects)
- **Word form → Translate:** saving a word opened from a Translate card returns to `/words`, not back to Translate. A validated `returnTo` query param would do it.
- **Model upgrade:** `claude-haiku-5-5` is about 10× cheaper than `claude-haiku-4-5`. Adaptive thinking is on by default there, which adds latency and spends `MaxTokens`, so lower the effort (or turn thinking off where the API allows) and re-measure latency. It's a config change plus a small request-shape change.
- **Two-stage translate** if real latency disappoints: return the plain translation first, then the cards.
- **Translation history** (searchable list of past lookups): explicitly out of scope for Translate mode v1.
- **EF Core `MultipleCollectionIncludeWarning`** in the API log on word queries: consider `AsSplitQuery()` where several collections are included.
