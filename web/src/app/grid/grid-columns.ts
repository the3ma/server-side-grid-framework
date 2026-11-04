import type { ColDef, ValueFormatterParams } from 'ag-grid-community';
import {
  GRID_FIELD_PATTERN,
  OPS_BY_TYPE,
  SCAN_OPS,
  type GridColumnType,
  type GridDateSemantics,
  type GridOp,
  type GridSort,
} from './grid-request.types';

/** Name under which the server grid registers the set/boolean filter component. */
export const SET_FILTER_NAME = 'gkSetFilter';

export interface GridColumnSpec<TRow> {
  /** Field id = AG Grid colId = server field id. camelCase only. */
  field: string;
  type: GridColumnType;
  headerName: string;
  /** Row property to display when it differs from `field`. */
  rowField?: keyof TRow & string;
  sortable?: boolean;
  filterable?: boolean;
  /** Text columns included in the quick search. */
  searchable?: boolean;
  /** Filtering this column is index-backed; lifts the large-dataset guard. */
  indexed?: boolean;
  /** Role-restricted: the server opens it per caller only. */
  restricted?: boolean;
  /** Narrow the operators to a subset of the type's operators. */
  ops?: readonly GridOp[];
  /** Required on date columns. */
  dateSemantics?: GridDateSemantics;
  /** Any other AG Grid option (width, renderer...). */
  colDef?: Partial<ColDef<TRow>>;
}

export interface ResolvedGridColumn<TRow> extends GridColumnSpec<TRow> {
  ops: readonly GridOp[];
  sortable: boolean;
  filterable: boolean;
  searchable: boolean;
  indexed: boolean;
  restricted: boolean;
}

export interface GridColumns<TRow> {
  grid: string;
  /** = the server's guard threshold; absent when the grid has no guard. */
  largeDatasetThreshold?: number;
  defaultSort: GridSort[];
  columns: readonly ResolvedGridColumn<TRow>[];
  byField: ReadonlyMap<string, ResolvedGridColumn<TRow>>;
}

export function gridColumn<TRow>(spec: GridColumnSpec<TRow>): ResolvedGridColumn<TRow> {
  if (!GRID_FIELD_PATTERN.test(spec.field)) {
    throw new Error(`Grid field id "${spec.field}" must be camelCase (${GRID_FIELD_PATTERN}).`);
  }
  const allowed = OPS_BY_TYPE[spec.type];
  const ops = spec.ops ?? allowed;
  const invalid = ops.filter((op) => !allowed.includes(op));
  if (invalid.length) {
    throw new Error(
      `Grid field "${spec.field}": operators ${invalid.join(', ')} are not valid for ${spec.type}.`,
    );
  }
  if (spec.type === 'date' && !spec.dateSemantics) {
    throw new Error(`Grid field "${spec.field}": date columns need dateSemantics.`);
  }
  if (spec.searchable && spec.type !== 'text') {
    throw new Error(`Grid field "${spec.field}": only text columns can be searchable.`);
  }
  return {
    ...spec,
    ops,
    sortable: spec.sortable ?? false,
    filterable: spec.filterable ?? false,
    searchable: spec.searchable ?? false,
    indexed: spec.indexed ?? false,
    restricted: spec.restricted ?? false,
  };
}

export function defineGridColumns<TRow>(definition: {
  grid: string;
  largeDatasetThreshold?: number;
  defaultSort?: GridSort[];
  columns: ResolvedGridColumn<TRow>[];
}): GridColumns<TRow> {
  const byField = new Map<string, ResolvedGridColumn<TRow>>();
  for (const column of definition.columns) {
    if (byField.has(column.field)) {
      throw new Error(`Grid "${definition.grid}": duplicate field "${column.field}".`);
    }
    byField.set(column.field, column);
  }
  return {
    grid: definition.grid,
    largeDatasetThreshold: definition.largeDatasetThreshold,
    defaultSort: definition.defaultSort ?? [],
    columns: definition.columns,
    byField,
  };
}

/** Returns a copy without the restricted columns (for callers the server does not open them to). */
export function withoutRestricted<TRow>(columns: GridColumns<TRow>): GridColumns<TRow> {
  return defineGridColumns({
    grid: columns.grid,
    largeDatasetThreshold: columns.largeDatasetThreshold,
    defaultSort: columns.defaultSort,
    columns: columns.columns.filter((c) => !c.restricted),
  });
}

export function dateZoneLabel(semantics: GridDateSemantics, timeZone: string): string {
  switch (semantics) {
    case 'utc':
      return 'Date filter uses UTC days';
    case 'local':
      return `Date filter uses your time zone (${timeZone})`;
    case 'calendar':
      return 'Date filter uses plain calendar days';
  }
}

export interface ColDefOptions {
  /** True while the server reports `capabilities.textFilters: disabled`. */
  guarded: boolean;
  timeZone: string;
}

/** AG Grid column definitions for the declaration. Guarded scan operators are removed while guarded. */
export function toColDefs<TRow>(
  columns: GridColumns<TRow>,
  options: ColDefOptions,
): ColDef<TRow>[] {
  return columns.columns.map((column) => {
    const field = (column.rowField ?? column.field) as ColDef<TRow>['field'];
    const base: ColDef<TRow> = {
      colId: column.field,
      field,
      headerName: column.headerName,
      sortable: column.sortable,
      filter: false,
    };
    if (column.type === 'date' && column.dateSemantics) {
      base.headerTooltip = dateZoneLabel(column.dateSemantics, options.timeZone);
      base.valueFormatter = (p: ValueFormatterParams<TRow>) =>
        formatDate(p.value, column.dateSemantics === 'utc' ? 'UTC' : options.timeZone);
    }
    if (column.filterable) {
      Object.assign(base, filterFor(column, options));
    }
    return { ...base, ...column.colDef };
  });
}

function filterFor<TRow>(
  column: ResolvedGridColumn<TRow>,
  options: ColDefOptions,
): Partial<ColDef<TRow>> {
  switch (column.type) {
    case 'text': {
      const ops = options.guarded
        ? column.ops.filter((op) => !SCAN_OPS.includes(op as never))
        : column.ops;
      return {
        filter: 'agTextColumnFilter',
        filterParams: { filterOptions: [...ops], maxNumConditions: 1, debounceMs: 400 },
      };
    }
    case 'number':
      return {
        filter: 'agNumberColumnFilter',
        filterParams: {
          filterOptions: [...column.ops],
          maxNumConditions: 1,
          inRangeInclusive: true,
          debounceMs: 400,
        },
      };
    case 'date':
      return {
        filter: 'agDateColumnFilter',
        filterParams: {
          filterOptions: [...column.ops],
          maxNumConditions: 1,
          inRangeInclusive: true,
        },
      };
    case 'set':
      return { filter: SET_FILTER_NAME, filterParams: { kind: 'set', field: column.field } };
    case 'boolean':
      return {
        filter: SET_FILTER_NAME,
        filterParams: { kind: 'boolean', field: column.field },
        cellRenderer: (p: { value: unknown }) =>
          p.value === true ? 'Yes' : p.value === false ? 'No' : '',
      };
  }
}

function formatDate(value: unknown, timeZone: string): string {
  if (typeof value !== 'string' || !value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat('en-GB', {
    timeZone,
    year: 'numeric',
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date);
}
