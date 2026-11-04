import type { GridColumns, ResolvedGridColumn } from './grid-columns';
import { GridRequestMappingError } from './grid-errors';
import {
  GRID_LIMITS,
  NON_SELECTIVE_OPS,
  SCAN_OPS,
  VALUELESS_OPS,
  type GridFilter,
  type GridOp,
  type GridRequest,
  type GridSort,
  type GridValuesRequest,
} from './grid-request.types';

/** AG Grid's sort model item. */
export interface AgSortItem {
  colId: string;
  sort: 'asc' | 'desc';
}

/** Model of the set/boolean filter component. `includeBlank` is only used by boolean columns. */
export interface SetFilterModel {
  filterType: 'set';
  values: string[];
  includeBlank?: boolean;
}

/** The subset of AG Grid filter models the mapping reads (simple and combined). */
export type AgFilterModel = Record<string, AgColumnFilter>;
export interface AgColumnFilter {
  filterType?: string;
  type?: string | null;
  filter?: string | number | null;
  filterTo?: number | null;
  dateFrom?: string | null;
  dateTo?: string | null;
  values?: string[];
  includeBlank?: boolean;
  operator?: 'AND' | 'OR';
  conditions?: unknown[];
}

export interface GridStateInput {
  startRow: number;
  pageSize: number;
  sortModel: AgSortItem[];
  filterModel: AgFilterModel;
  search?: string;
  includeTotal: boolean;
  /** The user's IANA time zone, sent only when a `local` date column is filtered. */
  timeZone: string;
}

/** AG Grid state -> GridRequest. Pure; throws GridRequestMappingError instead of guessing. */
export function toGridRequest<TRow>(
  columns: GridColumns<TRow>,
  state: GridStateInput,
): GridRequest {
  const filters = toFilters(columns, state.filterModel);
  const search = normalizeSearch(state.search);
  const request: GridRequest = {
    page: Math.floor(state.startRow / state.pageSize),
    pageSize: state.pageSize,
    sort: toSort(columns, state.sortModel),
    filters,
    includeTotal: state.includeTotal,
  };
  if (search) request.search = search;
  if (needsTimeZone(columns, filters)) request.timeZone = state.timeZone;
  return request;
}

/**
 * Request for the distinct values of one set column. The field's own filter is ignored.
 * On a guarded grid, when that filter was the only narrowing one, the search and the guarded
 * text filters are left out: the list is wider instead of drawing a refusal.
 */
export function toGridValuesRequest<TRow>(
  columns: GridColumns<TRow>,
  field: string,
  filterModel: AgFilterModel,
  search: string | undefined,
  timeZone: string,
): GridValuesRequest {
  const others: AgFilterModel = { ...filterModel };
  delete others[field];
  let filters = toFilters(columns, others);
  let effectiveSearch = normalizeSearch(search);

  if (columns.largeDatasetThreshold !== undefined && !isGridNarrowed(columns, filters)) {
    filters = Object.fromEntries(
      Object.entries(filters).filter(
        ([, f]) => !(f.type === 'text' && SCAN_OPS.includes(f.op as never)),
      ),
    );
    effectiveSearch = undefined;
  }

  const request: GridValuesRequest = { field, filters };
  if (effectiveSearch) request.search = effectiveSearch;
  if (needsTimeZone(columns, filters)) request.timeZone = timeZone;
  return request;
}

/** True when a selective filter on an indexed column narrows the request (the guard's rule). */
export function isGridNarrowed<TRow>(
  columns: GridColumns<TRow>,
  filters: Record<string, GridFilter>,
): boolean {
  return Object.entries(filters).some(([field, filter]) => {
    const column = columns.byField.get(field);
    return !!column?.indexed && !NON_SELECTIVE_OPS.includes(filter.op);
  });
}

function normalizeSearch(search: string | undefined): string | undefined {
  const trimmed = search?.trim();
  return trimmed ? trimmed.slice(0, GRID_LIMITS.maxSearchLength) : undefined;
}

function needsTimeZone<TRow>(
  columns: GridColumns<TRow>,
  filters: Record<string, GridFilter>,
): boolean {
  return Object.entries(filters).some(([field, filter]) => {
    const column = columns.byField.get(field);
    return (
      column?.type === 'date' &&
      column.dateSemantics === 'local' &&
      !VALUELESS_OPS.includes(filter.op)
    );
  });
}

function toSort<TRow>(columns: GridColumns<TRow>, sortModel: AgSortItem[]): GridSort[] {
  if (sortModel.length > GRID_LIMITS.maxSorts) {
    throw new GridRequestMappingError('unexpressible', 'sort');
  }
  return sortModel.map((item) => {
    const column = columns.byField.get(item.colId);
    if (!column?.sortable) throw new GridRequestMappingError('unknownField', item.colId);
    return { field: column.field, dir: item.sort };
  });
}

function toFilters<TRow>(
  columns: GridColumns<TRow>,
  model: AgFilterModel,
): Record<string, GridFilter> {
  const filters: Record<string, GridFilter> = {};
  for (const [field, raw] of Object.entries(model ?? {})) {
    if (!raw) continue;
    const column = columns.byField.get(field);
    if (!column?.filterable) throw new GridRequestMappingError('unknownField', field);
    if (raw.operator || raw.conditions)
      throw new GridRequestMappingError('multipleConditions', field);
    const filter = toFilter(column, raw);
    if (filter) filters[field] = filter;
  }
  return filters;
}

function toFilter<TRow>(
  column: ResolvedGridColumn<TRow>,
  raw: AgColumnFilter,
): GridFilter | undefined {
  switch (column.type) {
    case 'text': {
      expectType(column, raw, 'text');
      const op = expectOp(column, raw.type);
      if (VALUELESS_OPS.includes(op)) return { type: 'text', op };
      if (typeof raw.filter !== 'string' || raw.filter === '') return undefined;
      return { type: 'text', op, value: raw.filter };
    }
    case 'number': {
      expectType(column, raw, 'number');
      const op = expectOp(column, raw.type);
      if (VALUELESS_OPS.includes(op)) return { type: 'number', op };
      if (typeof raw.filter !== 'number') return undefined;
      if (op === 'inRange') {
        if (typeof raw.filterTo !== 'number') return undefined;
        return { type: 'number', op, value: raw.filter, valueTo: raw.filterTo };
      }
      return { type: 'number', op, value: raw.filter };
    }
    case 'date': {
      expectType(column, raw, 'date');
      const op = expectOp(column, raw.type);
      if (VALUELESS_OPS.includes(op)) return { type: 'date', op };
      const from = toDay(column, raw.dateFrom);
      if (!from) return undefined;
      if (op === 'inRange') {
        const to = toDay(column, raw.dateTo);
        if (!to) return undefined;
        return { type: 'date', op, value: from, valueTo: to };
      }
      return { type: 'date', op, value: from };
    }
    case 'set': {
      expectType(column, raw, 'set');
      // Nothing selected is sent as `in []` and matches nothing.
      return { type: 'set', op: 'in', values: [...(raw.values ?? [])] };
    }
    case 'boolean': {
      expectType(column, raw, 'set');
      return toBooleanFilter(column, raw.values ?? [], raw.includeBlank ?? false);
    }
  }
}

/**
 * Boolean columns use the set filter over true / false / blank:
 * one value -> equals, true + false -> notBlank, blank only -> blank, all three -> no filter.
 * Nothing selected, or one value plus blank, needs two conditions and fails loudly.
 */
function toBooleanFilter<TRow>(
  column: ResolvedGridColumn<TRow>,
  values: string[],
  includeBlank: boolean,
): GridFilter | undefined {
  const t = values.includes('true');
  const f = values.includes('false');
  if (t && f && includeBlank) return undefined;
  if (t && f) return { type: 'boolean', op: 'notBlank' };
  if (!t && !f && includeBlank) return { type: 'boolean', op: 'blank' };
  if ((t || f) && !includeBlank) return { type: 'boolean', op: 'equals', value: t };
  throw new GridRequestMappingError('unexpressible', column.field);
}

function expectType<TRow>(
  column: ResolvedGridColumn<TRow>,
  raw: AgColumnFilter,
  filterType: string,
): void {
  if (raw.filterType !== undefined && raw.filterType !== filterType) {
    throw new GridRequestMappingError('unsupportedFilter', column.field);
  }
}

function expectOp<TRow>(column: ResolvedGridColumn<TRow>, op: string | null | undefined): GridOp {
  if (!op || !column.ops.includes(op as GridOp)) {
    throw new GridRequestMappingError('unsupportedFilter', column.field);
  }
  return op as GridOp;
}

/** AG Grid gives `YYYY-MM-DD hh:mm:ss`; the date part is taken as text, never parsed into a Date. */
function toDay<TRow>(
  column: ResolvedGridColumn<TRow>,
  value: string | null | undefined,
): string | undefined {
  if (!value) return undefined;
  const day = value.slice(0, 10);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(day))
    throw new GridRequestMappingError('unsupportedFilter', column.field);
  return day;
}
