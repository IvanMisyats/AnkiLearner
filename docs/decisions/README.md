# Decisions

The history of why the app is built the way it is. Two kinds of documents live here:

- **[`ADR.md`](ADR.md)**: the decision log. One short, append-only record per significant
  decision or feature, covering context, decision and consequences. An accepted record is never
  rewritten; a later record supersedes it.
- **Design records** (`<feature>.md`): the long-form reasoning behind a big feature. Each covers
  what was measured, the options weighed, the plan in phases, and what changed while building it.
  The plan is written here first and kept current while the feature is built, then kept as the
  record. Each is linked from its ADR entry.

| Design record | Feature | ADRs |
|---|---|---|
| [`translate-mode.md`](translate-mode.md) | Translate mode (2026-11) | 0006–0009 |
| [`fsrs.md`](fsrs.md) | FSRS-6 scheduler, review history import (2026-11) | 0012–0014 |

Elsewhere in `docs/`: [`SPECIFICATION.md`](../SPECIFICATION.md) says *what* the app does now;
[`BACKLOG.md`](../BACKLOG.md) lists known defects and follow-ups;
[`IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) is the original v1 build plan.
