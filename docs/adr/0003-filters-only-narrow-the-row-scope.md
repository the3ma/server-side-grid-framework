# ADR-0003: Wrap the base query; filters can only narrow the row scope

**Status:** accepted

## Context
Row-level access (which tenant, which accounts, which regions a user may see) lives in each grid's base
query. If grid filters were spliced into that query's own `WHERE`, an `OR` or an operator-precedence
mistake could widen what a user sees.

## Decision
The handler owns only its base SQL: projection, joins and row scope bound to parameters taken from
the authenticated principal - never from the request body - with no `ORDER BY` and no paging. The
framework wraps it as a derived table `g` and ANDs every filter, the quick search, the sort (plus a
unique tiebreaker) and `OFFSET/FETCH` **outside** it. The count query runs over the same `WHERE`, and
only when the client asks for a total.

## Consequences
- A filter can only ever remove rows from what the user is already allowed to see.
- Paging is stable: every sort ends with a unique tiebreaker.
- Grouping and aggregation must happen inside the base SQL; the framework filters and sorts the
  projection, it does not group.
