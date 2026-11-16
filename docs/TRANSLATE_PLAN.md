# Translate mode — implementation plan

> Durable plan for the `feature/translate-mode` branch. Source of truth for design decisions and
> phase progress; update the checklist at the end of every phase.

## Progress

- [x] Phase 1a — Matching, new-card order, relearn (backend)
- [x] Phase 1b — Translate backend
- [x] Phase 2 — Translate page (frontend)
- [x] Phase 3 — Home selector, routing, returnUrl
- [x] Phase 4 — Android PWA
- [x] Verification, local (2026-11-16):
  - Full suites green: backend 233, frontend 108; production build clean.
  - 16/16 end-to-end checks pass against the real API and Postgres with a fake Anthropic server, in headless Chrome at 360/320 px.
- [ ] Verification after deploy:
  - Purge the Cloudflare cache.
  - Real AI quality and latency on a few inputs.
  - On-device: install, shortcuts, Share → AnkiLearner.

### Deviations from the plan

- **Phase 1a review:**
  - The relearn state change lives in Core as `Sm2.Relearn`.
  - Relearn never moves an overdue card later (`Due = min(Due, now)`).
  - New-card order is made stable with a tie-break on `Id`, because imported words share one `CreatedAt`.
- **Phase 1b reviews (2 rounds):**
  - **Input kind:** the shaper trusts the model's kind. A sentence without spaces, such as "Tak!" or a Japanese sentence, kept no examples otherwise.
  - **Homographs:**
    - Exact-term duplicates merge their meanings, so "en kost" keeps both broom and diet.
    - The prompt asks for one card per citation form.
    - A saved word that exactly matches one card is not used as an article-insensitive match for another card ("gift" vs "en gift").
  - **Citation form:** the article/infinitive-marker rule and the Danish examples apply only to languages with a particle table (Danish). Other learning languages get the lemma.
  - **Input limits:** at most 1000 raw characters and 300 after cleaning. Cleaning drops invisible format characters (`\p{Cf}`) and turns control characters into spaces.
  - **Whole input:** never offered for single-token input. Trailing punctuation is ignored when comparing ("Tak for mad!" = "tak for mad") and dropped from a Phrase term.
  - **Request handling:**
    - The source-language override is case-insensitive.
    - Ties between existing-word matches are broken by `Id`.
    - Malformed model JSON is reported without quoting it.
    - The lookup endpoint's cancellation handling matches translate.
- **Phase 2 scope addition:** the word form gets a **Study again** (relearn) button next to Reset progress. With newest-first ordering, Reset now sends an old word behind the whole new-card backlog.
- **Study again has no confirm dialog:** relearn deletes nothing (it only makes the word due now). The confirm dialog stays on Reset progress.
- **Phase 2 review:**
  - The kept Translate result is keyed to the user and their languages and resets for a different owner; late responses for an earlier owner are dropped.
  - The 429 message is neutral ("Too many requests"), because login is rate-limited too.
  - The toolbar was made to fit 360 px here; it already overflowed before this feature.
- **Phase 3:**
  - **Import left the toolbar:** an "Import from Anki" button sits on the Dictionary page instead, so the toolbar fits 320 px with 44 px tap targets. The toolbar is Translate, Learn, Dictionary, Settings and Sign out, with an active pill and `aria-current`.
  - **The dashboard heading is "Learn"** (was "Study") to match the toolbar.
  - **Login redirect:**
    - Login/register return with `replaceUrl`, so Back doesn't show the login form.
    - `/login` and `/register` are never accepted as a returnUrl.
    - A session expiry keeps the current page (the Translate page restores its text from its state service).
  - **Home:** a zero backlog reads "Nothing due right now".
  - **Reviewer check:** about 45 hostile returnUrl values went through Angular's own URL parser; none left the app.
- **Phase 4:**
  - **Theme colour:** `theme_color` is the toolbar's surface colour `#faf9fd`, so the Android status bar blends with the toolbar. The brand blue `#005cbb` is in the icons.
  - **Share target:** has `enctype` set to silence a Chrome manifest warning.
  - **Icons:** four SVG sources (app, maskable, two shortcuts) plus the rendered PNGs. The old Angular-logo `favicon.ico` was replaced with one rendered from the app icon.
  - **`index.html` caching:** went from `no-cache, no-store, must-revalidate` to `no-cache`, so ETag 304s now work.
  - **Local dev side effect:** `docker compose up -d db` replaced a stale stopped `ankilearner-db` container with `ankilearner-db-dev`. It is the same volume, so no data was lost.

## Context
Today AnkiLearner only serves **learning** (SRS study). Ivan mostly meets unknown Danish words on his
**Android phone** and wants a second mode, **Translate**: open the site, type a DA or EN word, phrase or
sentence, see the translation with examples, and add Danish vocabulary from it to the SRS dictionary in
one tap. The existing AI "Look up" in the add-word form already produces full Danish word cards, but it
only works DA → known and is buried in the form. Translate mode generalizes it. No DB schema change or
migration is needed.

## Decisions (grilling + architecture review)
- `/` = **mode selector**: a Translate card and a Learn card (due + new counts). The study dashboard moves to `/study`.
- **Source language**:
  - Auto-detected among the learning + known languages; ambiguous words (gift, kind, barn) default to Danish.
  - Manual override: Auto / DA / EN / UK.
  - Any other language → "Only Danish, English, Ukrainian are supported".
- Input is at most **one sentence** (300 chars).
- **Result**: the input's translation + **AI-picked cards**. Each card is a Danish entry: citation form, IPA, part of speech, gender, meanings per known language, example + translations, and a short note.
  - EN → DA: the Danish alternatives (know → vide / kende / kunne), with notes on how they differ.
  - Sentence: the vocabulary worth learning; idioms kept whole.
  - Multi-word input also gets **"Add whole input"**.
- **Saved term = citation form**: "en aftale", "at spise". The gender field is still filled. The look-up button in the add-word form switches to the same form, so both ways of adding agree.
- **Sentence input**: every card's example is **Ivan's own sentence**. For EN input it's the Danish translation, with his English original as the EN example translation.
- **One-tap Add** (`POST /api/words`):
  - Toast "Added · Edit".
  - Auto-tag `translated`; the note goes into Notes.
  - Languages with no meanings are left out of the request.
  - All AI text is HTML-escaped before saving.
- **Existing words**:
  - Card shows "In your dictionary" (link), **Study again**, and **Add anyway** for false matches.
  - Matching **ignores a leading en/et/at**. Your data is mixed: ~290 nouns with an article, 39 verbs with "at".
  - The same matching fixes the add-word form's duplicate warning.
- **New cards newest-first** (was oldest-first), so words met today come up tomorrow.
- **Study again = "relearn now"**, *not* reset. With newest-first ordering, a reset old word would drop to the back of the new-card queue.
  - Existing direction states: Repetitions = 0, Interval = 0, Lapses += 1, Due = now.
  - Directions never studied: create a state that is due now.
  - Result: the word comes up in the next session in both directions.
- **Last result kept in memory** (root service). Back / Edit → return doesn't re-run a paid AI call. This is view state, not history; there's still no translation history.
- AI unavailable → a message + a manual-add link. Translate shares the per-user `lookup` rate limit; on 429 the message reads "Too many requests — try again in a minute".
- Last phase: **Android installable app (PWA)**, with Translate / Learn shortcuts and a share target.

## Architecture (verdict: sound; review changes folded in)
- **Backend**: `IWordLookupProvider` gains `TranslateAsync`.
  - It lives in the same `ClaudeLookupProvider`, with its own prompt/schema built by static, unit-tested builders.
  - The lookup and translate schemas share one card-schema builder and one citation-form rule.
- **Schema stability**: every user always gets the same schema (source `enum` = [learning, …known, other]).
  - The override goes in the user message, not as a schema `const`. Each new schema pays an API compile cost (cached 24h).
  - The API ignores min/max items limits, so the server enforces the cap of 5 cards.
- **Pure `TranslateShaper` in Core** does all the deterministic rules. The controller only validates input, calls the provider, shapes the result, and flags existing words.
  - Rules: override; trust the model's input kind; fill in the source-language slot; drop empty cards (incl. HTML-only); terms made plain text; exact-term duplicates **merge their meanings** (keeps homographs "gift" / "en gift" as separate cards); cap 5 cards and 4 meanings per language; clamp field lengths; sentence example override; build WholeInput (never for single-token input; trailing punctuation ignored).
- **Existing-word flags**:
  - One indexed query `TermNormalized IN (candidates)` over the existing `(UserId, LanguageCode, TermNormalized)` index.
  - Candidates come from `TermNormalizer.MatchCandidates`, using a per-language particle table: `da` → en/et/at. Other languages keep exact matching, so the feature stays generic.
  - Exact matches win.
- **Robustness**:
  - `MaxTokens` 1024 → 4096 (it's only a cap).
  - Explicit 502 on `max_tokens`/`refusal`; log the reason and token count, never the user's text.
  - Anthropic client timeout ~30 s, 1 retry.
  - Server-side trim, collapse whitespace, and 400 on empty input.
- **Latency risk**: on Haiku 4.5, roughly 2–4 s for a word and up to ~7–12 s for a 5-card sentence.
  - Mitigations: "usually 1–3 cards", at most 3 meanings per language, one-sentence notes, no AI examples for sentence input, and a clear loading state.
  - Measured in verification. A two-stage translate or a model upgrade are follow-ups if needed.
- **Safety**: AI text is rendered only with `{{ }}`, never `[innerHTML]`. It is escaped client-side before saving, and the server sanitizes on save as it does today.

## Phases
Each phase is built by a **fresh implementer agent**, then checked by a **fresh reviewer agent** (read-only, adversarial). I triage the findings, the implementer fixes them, tests go green, and I commit. Review depth matches the risk.

### Phase 1a — Matching, new-card order, relearn (backend) · 1 review round
- `Core/TermNormalizer.cs`: add `MatchKey(term, lang)` and `MatchCandidates(term, lang)`. A particle is stripped only when followed by a space and something more ("enkel", "atom" and a bare "en" stay unchanged).
- `WordsController.CheckDuplicate`: use the candidates and prefer an exact match. This also makes the result deterministic; today it's `FirstOrDefault` with no ordering.
- `StudyController.BuildNextAsync`: new cards ordered by `CreatedAt` **descending**.
- New `POST /api/words/{id}/relearn` with the relearn semantics above, scoped by `UserId`.
- **Tests**:
  - New `Unit/TermNormalizerTests`.
  - `WordsCrudTests`: duplicate check ignores the article; `excludeId` still works.
  - `StudyFlowTests`: newest-first.
  - Relearn: makes the word due in both directions; 404 for another user's word.
- Spec FR-W7, FR-R4, FR-D8 and the API list.

### Phase 1b — Translate backend · 2 adversarial review rounds (untrusted AI output, prompt/schema, data scoping)
- **Core**: `InputKind { Word, Phrase, Sentence }`, `TranslateCard`, `TranslateResult`, `IWordLookupProvider.TranslateAsync(text, sourceLanguage?, target, known, ct)`, and the static `Core/TranslateShaper.Shape(...)` returning `(result, wholeInput?)`.
  - Length limits move to constants on `Word`, used by `AppDbContext`, the `[MaxLength]` attributes and the shaper.
- **Infrastructure** (`ClaudeLookupProvider`):
  - New `BuildTranslateSystemPrompt`, `BuildTranslateSchema`, `ParseTranslateResult`, and a stop-reason check.
  - Shared card-schema builder; the lookup prompt switches to the citation form.
  - `AnthropicOptions.MaxTokens` = 4096; `TimeoutSeconds` = 30, documented in README.
- **Api**: `Contracts/TranslateContracts.cs` with `TranslateRequest([Required, MaxLength(1000)] Text, [MaxLength(20)] SourceLanguage?)` (raw cap 1000; ≤ 300 after cleaning), `TranslateCardDto` (+ `ExistingWordId`) and `TranslateResponse(SourceLanguage, InputKind, InputTranslations, Cards, WholeInput?)`.
  - The names avoid colliding with the existing `TranslationDto`.
- **`Controllers/TranslateController.cs`**: `POST /api/translate`, `[Authorize]`, `[EnableRateLimiting("lookup")]`.
  - Responses: 503 when unavailable, 400 on bad input or source language, 422 for "other", 502 when the provider fails.
- **Tests**:
  - **Fake provider**: `FakeLookupProvider` gets a settable translate function and records the arguments it was called with.
  - **Unit**:
    - Schemas: walk them recursively to check `additionalProperties:false` everywhere and no unsupported keywords.
    - Enums and prompt content.
    - Tolerant parsing, stop-reason handling, and every `TranslateShaper` rule (homographs, clamps, both sentence directions, WholeInput present/omitted).
  - **Integration (new `TranslateEndpointTests`)**:
    - 401 / 503 / 502 / 400 / 422.
    - The override reaches the provider.
    - `ExistingWordId` is set when only the article differs.
    - No match against another user's words or another learning language.
    - Sentence example and WholeInput.
    - The rate limit is shared with lookup (`LookupPerMinute=2` → the third call returns 429).
- **Docs**: new spec section §3.8 Translate (FR-T*); FR-W2 citation form; API list; §8 privacy line updated.

### Phase 2 — Translate page (frontend) · 1 review round (focus: HTML escaping, Angular clarity)
- **`core/api.types.ts` + `api.services.ts`**: `InputKind` (`'Word'|'Phrase'|'Sentence'`), `TranslateCardDto`, `TranslateResponse`, `TranslateApi.translate(text, sourceLanguage|null)`, and `WordsApi.relearn(id)`.
- **New `core/word-html.ts`** with `escapeHtml` and `meaningsToHtml`. `word-form` uses it, which fixes today's unescaped single meaning, example and term.
- **New `features/translate/`**:
  - `translate.component.ts`: the page. It owns the API calls; reads `?q=`, then clears it with `replaceUrl`; shows idle / loading / unavailable / error states; has the language toggle; a phone-friendly input (`enterkeyhint="go"`, no autocorrect or autocapitalize, Enter submits).
  - `translate-card.component.ts`: display only, with the `new / adding / added / existing` states.
  - `translate-state.service.ts`: the root service that keeps the last result in memory.
  - `save-request.ts`: pure `toSaveWordRequest`.
  - `shared-text.ts`: `extractSharedText` cleans text shared from Android (quotes, URLs, whitespace, 300-char cap).
- **`notify.service.ts`**: `successWithAction(...)` (the snackbar "Edit" action) and the 429 message.
- **`app.routes.ts`**: `translate` route.
- **422 (unsupported language)**: show the server's message and suggest the language override. A misspelled or Norwegian-looking Danish word can be misdetected as "other".
- **`word-form.component.ts`**: a **Study again** button (`WordsApi.relearn`) next to Reset progress, disabled while its request runs.
- **CI**: add `npx ng test --watch=false`. CI currently runs only the frontend build, so the new frontend tests wouldn't run in CI.
- **Specs** (existing TestBed + `HttpTestingController` + `RouterTestingHarness` patterns):
  - word-html, save-request, shared-text.
  - Translate page:
    - `?q=` sends a request automatically.
    - Add sends the right body and switches the card to "Added".
    - Existing / Add anyway / Study again (no confirm: relearn is not destructive).
    - 503 state.
    - Whole input.
    - Changing the override re-submits.
    - State is restored without a new request.

### Phase 3 — Home selector, routing, returnUrl · 1 review round (focus: open redirect)
- **New `features/home/home.component.ts`** at `''`: Translate card; Learn card summing due/new over both directions; AI-unavailable hint.
- **Routes**: the dashboard moves to `study`.
- **Toolbar**: Translate, Learn (`/study`), Dictionary, Import, Settings. Must fit a 360 px screen.
  - From the Phase 2 review: icon links are only ~34 px wide with no gap (below the 48 dp tap guideline), the bar overflows by 7 px at 320 px, and there's no `.active` style, so with icon-only nav you can't tell which page you're on. Fix all three here. Phase 2 only made the bar fit at 360 px (`app.scss`, < 700 px).
- **Links**: study "Back to dashboard" and import "Start studying" → `/study`.
- **Login keeps the original link** (`returnUrl`):
  - `auth.guard` passes `returnUrl`; login/register accept it only if it starts with `/` and not `//` or `/\`.
  - `sessionExpired` keeps the current URL. Without this, a shared word is lost when the login screen interrupts it.
- **Specs**: home sums the counts; the guard passes `returnUrl`; login rejects `//evil.com` and `https://evil.com`.

### Phase 4 — Android PWA · 1 review round
- **`public/manifest.webmanifest`**:
  - `id` / `start_url` / `scope` "/", standalone display, name AnkiLearner, theme colors.
  - Icons 192 / 512 / 512-maskable; shortcuts Translate `/translate` and Learn `/study`.
  - `share_target` GET → `/translate`, params `title` / `text→q` / `url`.
  - Icons are generated from a simple SVG (replaceable).
- **`index.html`**: manifest link + theme-color. **No service worker**: Chrome's installability criteria no longer require one.
- **`Program.cs` cache fix (pre-existing bug)**:
  - Today every static file except `index.html` is marked `immutable` for a year: manifest, icons and favicon would be pinned in browsers and at Cloudflare.
  - Deep-link `index.html` from the fallback is sent with no Cache-Control.
  - Change: immutable only for `.js`/`.css`; `no-cache` for everything else; pass the same options to `MapFallbackToFile`. Unit-test the helper.
- **Translate page**: reads `q`, then `title`, then `url` through `extractSharedText`. After use it clears **all three** params (Phase 2's `translateShared` clears only `q`).
- `infra/README.md`: on-device smoke test.
- ~~Ivan checks the CSP~~ — **resolved in review:** the live CSP is only `frame-ancestors 'self'`, so the manifest isn't blocked.
- **One-time post-deploy step:** purge the Cloudflare cache (`/favicon.ico`, `/icons/*`, `/manifest.webmanifest`, or everything). The old favicon was served `immutable` for a year, so the new link uses `favicon.ico?v=2`.

## Execution process
1. Check Docker is running (backend tests use Testcontainers). Branch `feature/translate-mode` off `main`.
2. Write `docs/TRANSLATE_PLAN.md` (this plan + a live checklist) and commit it.
3. Per phase:
   - The implementer agent builds the code, tests and docs per CLAUDE.md, without committing.
   - The reviewer agent reviews the uncommitted diff (1b gets two rounds).
   - Fixes; `dotnet test` and/or `npx ng test --watch=false` + `npm run build` go green.
   - I commit one phase = one commit and tick the plan doc's checklist.
4. Spec / IMPLEMENTATION_PLAN / CLAUDE.md status / README are updated within each phase.
5. **No pushing by me.** At the end Ivan pushes the branch. Squash merge to main per his git rules.

## Verification
- Full `dotnet test`, `npx ng test --watch=false`, `npm run build`.
- Run locally (`docker compose up -d db`, `dotnet run`, `npm start`) and drive Chrome at a 360 px phone viewport. Check:
  - Home selector.
  - Translate: "hund", "maven", "Jeg har ondt i maven", "know", "gift" with the override, a Ukrainian word, German → error.
  - Add, then the word appears in the dictionary as "en mave" tagged `translated`, and is studied next as a new card.
  - An existing word is flagged; Study again makes it due now.
  - Back keeps the result without a new call; AI unavailable state.
- Real AI calls need the Anthropic key in user-secrets (fractions of a cent each). Time them to confirm the latency estimate.
- After Ivan deploys: on-device check — install, shortcuts, share text from Chrome → translation.

## Out of scope / backlog
Tracked in [`BACKLOG.md`](BACKLOG.md): import duplicate detection (exact-match), word form `returnTo`,
a `claude-haiku-5-5` upgrade, two-stage translate, translation history, and the defects found in review.
The decisions are summarised in [`ADR.md`](ADR.md) (ADR-0006 to 0009).
