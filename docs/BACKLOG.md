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
- **Fix idea:** use `TermNormalizer.MatchCandidates` in `ImportController`'s duplicate check, preferring an exact match like `WordsController.CheckDuplicate`. The "History only" matching (FR-I9) is exact too: a word edited to its citation form since the import ("aftale" → "en aftale") gets no history.

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

### FSRS days start at UTC midnight, not at the user's local 04:00
- **Found:** 2026-11-24, FSRS Phase 2 review.
- **Symptom:** FSRS counts elapsed time in calendar days (as Anki does), but splits them at UTC midnight, the app's day everywhere (FR-R1, FR-R7). Anki splits at 04:00 local time. For a European user the two hardly ever split a session differently. For a user in UTC−5, two reviews at 18:50 and 19:10 local fall on different UTC days, so the second one gets the long-term formula: a 20-minute gap counts as a day and Stability jumps.
- **Fix idea:** a per-user day start (time zone + rollover hour, e.g. sent by the browser) used by `Fsrs.ElapsedDays` and the daily counters alike.

## Follow-ups (improvements, not defects)
- **Word form → Translate:** saving a word opened from a Translate card returns to `/words`, not back to Translate. A validated `returnTo` query param would do it.
- **Two-stage translate** if real latency disappoints: return the plain translation first, then the cards.
- **Translation history** (searchable list of past lookups): explicitly out of scope for Translate mode v1.
- **EF Core `MultipleCollectionIncludeWarning`** in the API log on word queries: consider `AsSplitQuery()` where several collections are included.
