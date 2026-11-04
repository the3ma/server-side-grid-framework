/**
 * The grid contract (docs/CONTRACT.md) as TypeScript types.
 * Everything a server-side grid sends or receives is one of these shapes.
 */

export type GridColumnType = 'text' | 'number' | 'date' | 'set' | 'boolean';
export type GridSortDir = 'asc' | 'desc';
export type GridDateSemantics = 'utc' | 'local' | 'calendar';

export const TEXT_OPS = [
  'contains',
  'notContains',
  'equals',
  'notEqual',
  'startsWith',
  'endsWith',
  'blank',
  'notBlank',
] as const;

export const NUMBER_OPS = [
  'equals',
  'notEqual',
  'lessThan',
  'lessThanOrEqual',
  'greaterThan',
  'greaterThanOrEqual',
  'inRange',
  'blank',
  'notBlank',
] as const;

export const SET_OPS = ['in'] as const;
export const BOOLEAN_OPS = ['equals', 'blank', 'notBlank'] as const;

export type TextOp = (typeof TEXT_OPS)[number];
export type NumberOp = (typeof NUMBER_OPS)[number];
export type GridOp = TextOp | NumberOp | (typeof SET_OPS)[number] | (typeof BOOLEAN_OPS)[number];

/** Operators allowed for each column type. Dates use the number operators. */
export const OPS_BY_TYPE: Readonly<Record<GridColumnType, readonly GridOp[]>> = {
  text: TEXT_OPS,
  number: NUMBER_OPS,
  date: NUMBER_OPS,
  set: SET_OPS,
  boolean: BOOLEAN_OPS,
};

/** Text operators that need a full scan; refused on a large grid until it is narrowed. */
export const SCAN_OPS: readonly TextOp[] = ['contains', 'notContains', 'endsWith'];

/** Operators that never count as "narrowing" for the large-dataset guard. */
export const NON_SELECTIVE_OPS: readonly GridOp[] = [...SCAN_OPS, 'blank', 'notBlank', 'notEqual'];

/** Operators that take no value. */
export const VALUELESS_OPS: readonly GridOp[] = ['blank', 'notBlank'];

export const GRID_LIMITS = {
  maxPageSize: 500,
  defaultPageSize: 100,
  maxSorts: 3,
  maxFilters: 50,
  maxSetValues: 1000,
  maxSearchLength: 200,
  maxTextValueLength: 200,
  maxDistinctValues: 1000,
  minDate: '1753-01-01',
} as const;

/** Field ids are camelCase on both sides (the server camel-cases error keys). */
export const GRID_FIELD_PATTERN = /^[a-z][A-Za-z0-9]{0,63}$/;

export interface GridSort {
  field: string;
  dir: GridSortDir;
}

export interface GridFilter {
  type: GridColumnType;
  op: GridOp;
  value?: string | number | boolean;
  valueTo?: string | number;
  values?: string[];
}

export interface GridRequest {
  page: number;
  pageSize: number;
  sort: GridSort[];
  filters: Record<string, GridFilter>;
  search?: string;
  timeZone?: string;
  includeTotal: boolean;
}

export interface GridValuesRequest {
  field: string;
  search?: string;
  filters: Record<string, GridFilter>;
  timeZone?: string;
}

export interface GridCapabilities {
  textFilters: 'enabled' | 'disabled';
  reason?: 'resultSetTooLarge';
  threshold?: number;
}

export interface GridPage<TRow> {
  rows: TRow[];
  /** `null` = not computed (paging without a recount). Never shown as 0. */
  totalCount: number | null;
  capabilities: GridCapabilities;
}

export interface GridValuesPage {
  values: string[];
  truncated: boolean;
}
