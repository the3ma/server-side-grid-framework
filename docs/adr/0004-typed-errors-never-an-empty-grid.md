# ADR-0004: Typed, value-free errors; a failure is never an empty grid

**Status:** accepted

## Context
Grids used to catch an error and show zero rows. In a regulated data portal an empty grid reads as
"there is no data", which is a data-integrity problem, not a cosmetic one. And error messages that
echo a filter value put that value into logs and monitoring.

## Decision
Grid failures are RFC 7807 ProblemDetails with four types: `grid-request-invalid` (per-field
`errors` and `errorCodes`), `grid-filter-too-broad` (`fields`, `threshold`), `grid-query-timeout`
(504) and `grid-query-failed` (500). Messages and keys never contain request values; a malformed field
key is reported as `(invalid field)`. A database value-conversion error is re-thrown value-free and
the driver exception, which can echo a value, is never chained or logged. The frontend maps each
type to a typed error `kind` and shows an inline `role="alert"` banner with "Try again".

## Consequences
- Users always know a load failed; the grid never pretends to be empty.
- Logs carry the grid id, field ids, operators and counts only - no filter values.
- Server faults (504/500) are reported to monitoring; client errors (400) are not.
