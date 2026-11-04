# ADR-0002: The large-dataset guard is a server rule, not a UI rule

**Status:** accepted

## Context
`contains`, `notContains`, `endsWith` and a free-text quick search cannot use an index: on a table
with millions of rows each one is a full scan. Disabling them in the UI protects nobody who calls the
API directly, and silently slow grids are worse than refused ones.

## Decision
A grid opts in with `GuardScanFilters(threshold)`. Scan-type filters and the quick search are then
refused with `400 grid-filter-too-broad` (naming the fields and the threshold) unless the same
request also has a **selective** filter on an `Indexed()` column, or the server knows the scoped row
count is at or under the threshold. Every page reports `capabilities.textFilters`, so the UI removes
the guarded operators and explains why in an `aria-live` hint.

## Consequences
- The rule holds for every caller, including scripts and future clients.
- Users get an explanation and a way forward ("filter by warehouse or status first") instead of a
  spinner.
- `Indexed()` is trusted: on Oracle a mixed-case text column is compared as `UPPER(col)`, so it needs a
  function-based index or a `StoredUpperCase()` declaration, otherwise the guard is lifted while the
  query still scans. This is documented per column.
