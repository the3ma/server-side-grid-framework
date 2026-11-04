# ADR-0006: A total is null when not counted; a malformed page is an error

**Status:** accepted

## Context
Counting a large filtered set can cost as much as the page itself, and it is wasted work while the
user just pages forward. But a "0" shown for a total that was never computed is a false statement.

## Decision
The count query runs only when `includeTotal` is true. Otherwise `totalCount` is `null`, never `0`,
and the client keeps the last known total. Every page is parsed strictly against the row schema on
the client: one row that does not match fails the whole page instead of rendering a partial grid.

## Consequences
- Paging forward costs one query, not two.
- No number on the screen is ever invented: unknown stays unknown.
- A contract drift between backend and frontend surfaces as a visible error at once, and a manifest
  test catches it before release.
