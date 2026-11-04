# ADR-0001: A per-grid column allow-list; no request value ever becomes SQL

**Status:** accepted

## Context
Every server-side grid used to build its own SQL from the request: a sort field here, a filter
operator there, each with its own escaping rules. A grid's request names columns, operators and
directions chosen by the browser, so every hand-written builder was a fresh chance of SQL injection,
and no two grids accepted the same request.

## Decision
Each grid declares a static `GridColumnMap`: field id -> **constant** SQL expression, type, allowed
operators and opt-in flags (`Sortable`, `Filterable`, `Searchable`, `Indexed`, `Restricted`). A
request can only *choose* among those entries. Fields, operators and sort directions resolve through
the map; every value is bound as a generated parameter (`grid_p0`, `grid_p1` ...). The SQL text
depends only on the request's *shape*, never on its values.

## Consequences
- Injection is impossible by construction, not by review: SQL snapshot tests assert that request
  values never reach the SQL text on any dialect, and guard tests reject unknown or malformed field
  ids before any SQL is built.
- A column must be a SQL expression over the base query. A value computed in application code after
  the query cannot be sorted or filtered on the server; it has to move into SQL first.
- Field ids are camelCase on both sides, enforced at definition time, so error keys survive any JSON
  serializer that camel-cases dictionary keys and the UI can always highlight the right column.
