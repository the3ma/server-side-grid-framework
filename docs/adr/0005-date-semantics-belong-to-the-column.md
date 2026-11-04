# ADR-0005: Date semantics belong to the column, not the client

**Status:** accepted

## Context
"Received on 3 March" means different instants for a user in Tokyo and one in New York. For an
operational date that is what the user expects; for a regulated record date it must not depend on
who is looking. Browsers that parse dates shift them silently by the local offset.

## Decision
Each date column declares `utc` (regulated records: UTC day boundaries, the client time zone is
ignored), `local` (operational dates: the user's local midnight in their IANA `timeZone`, converted
to UTC, DST-safe) or `calendar` (plain dates). The client sends `YYYY-MM-DD` strings taken straight
from the filter model, never parsed into a `Date`. Every operator is half-open:
`equals d` -> `>= start(d) AND < start(d+1)`.

## Consequences
- Regulated dates filter identically for every user; operational dates match the user's calendar.
- The grid shows which zone each date filter uses.
- `timeZone` is sent only when a `local` column is filtered, and is required then.
