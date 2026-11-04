/**
 * In-memory grid backend for the demo (`npm run start:mock`) and for tests. It applies the same
 * rules as the API: the column allow-list, limits, restricted columns, the large-dataset guard,
 * half-open date days and `totalCount: null` when no total was asked for. Data is synthetic.
 */
import type { GridColumns } from '../grid/grid-columns';
import {
  GRID_FIELD_PATTERN,
  GRID_LIMITS,
  NON_SELECTIVE_OPS,
  SCAN_OPS,
  VALUELESS_OPS,
  type GridFilter,
  type GridRequest,
  type GridValuesRequest,
} from '../grid/grid-request.types';
import { SHIPMENTS_GRID } from '../shipments/shipments.grid';
import type { ShipmentRow } from '../shipments/shipment-row.schema';

export interface MockCaller {
  tenant: number;
  finance: boolean;
}

export interface MockResponse {
  status: number;
  body: unknown;
}

type Row = ShipmentRow & { tenantId: number };

const STATUSES = ['received', 'in_transit', 'delivered', 'returned', 'cancelled'];
const CARRIERS = ['Northline', 'Parcelway', 'Bluecrate', 'Swiftlane', 'Overland', null];
const DESTINATIONS = [
  'Northgate Outfitters',
  'Riverside Kitchenware',
  'Lakeview Hardware',
  'Harbor Point Books',
  'Summit Cycle Works',
  'Oak Valley Garden Supply',
  'Westfield Furniture',
  'Eastbrook Home Goods',
];
const PRICE_TIERS = ['standard', 'discount', 'contract'];
const TENANT_SIZES: Record<number, number> = { 1: 80_000, 2: 20_000, 3: 20_000 };

/** Deterministic PRNG (mulberry32) so every run has the same rows. */
function prng(seed: number): () => number {
  let a = seed;
  return () => {
    a |= 0;
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function generateRows(sizes: Record<number, number> = TENANT_SIZES): Row[] {
  const random = prng(20240611);
  const pick = <T>(list: readonly T[]) => list[Math.floor(random() * list.length)];
  const rows: Row[] = [];
  const start = Date.UTC(2024, 0, 1);
  const span = Date.UTC(2025, 11, 31) - start;
  let id = 1;
  for (const [tenant, size] of Object.entries(sizes)) {
    for (let i = 0; i < size; i++, id++) {
      const shipped = start + Math.floor(random() * span);
      const status = pick(STATUSES);
      const delivered =
        status === 'delivered' || status === 'returned'
          ? shipped + Math.floor(random() * 4 * 86_400_000)
          : null;
      rows.push({
        tenantId: Number(tenant),
        shipmentId: id,
        trackingNo: `TRK-${String(id).padStart(7, '0')}`,
        warehouseCode: `WH-${100 + Math.floor(random() * 11)}`,
        destination: pick(DESTINATIONS),
        status,
        carrier: pick(CARRIERS),
        weightKg: random() < 0.05 ? null : Math.round((0.5 + random() * 29.5) * 10) / 10,
        shippedOn: iso(shipped),
        deliveredOn: delivered === null ? null : iso(delivered),
        flagged: random() < 0.08,
        priceTier: pick(PRICE_TIERS),
      });
    }
  }
  return rows;
}

function iso(ms: number): string {
  return new Date(ms).toISOString().slice(0, 19) + 'Z';
}

/** The server's view of each column: which row property it reads. */
const PROPERTY: Record<string, keyof Row> = Object.fromEntries(
  SHIPMENTS_GRID.columns.map((c) => [c.field, (c.rowField ?? c.field) as keyof Row]),
);

class Invalid {
  readonly errors: Record<string, string[]> = {};
  readonly codes: Record<string, string[]> = {};
  add(key: string, code: string, message: string): void {
    (this.errors[key] ??= []).push(message);
    (this.codes[key] ??= []).push(code);
  }
  get any(): boolean {
    return Object.keys(this.errors).length > 0;
  }
}

export class MockGridBackend {
  private readonly columns: GridColumns<ShipmentRow> = SHIPMENTS_GRID;

  /**
   * `knowsScopedSize`: like the API's KnownRowCount, lift the guard when the scoped set is at or
   * under the threshold. Off by default, as in the example API (the scoped size is not known up front).
   */
  constructor(
    private readonly rows: Row[] = generateRows(),
    private readonly options: { knowsScopedSize?: boolean } = {},
  ) {}

  page(request: GridRequest, caller: MockCaller): MockResponse {
    const invalid = new Invalid();
    this.validateShape(request, invalid);
    this.validateFilters(request.filters ?? {}, caller, invalid, request.timeZone);
    for (const s of request.sort ?? []) {
      const c = this.column(s.field, caller);
      if (!c) invalid.add(this.key(s.field), 'unknownField', 'Unknown field.');
      else if (!c.sortable) invalid.add(s.field, 'notSortable', 'This field cannot be sorted.');
    }
    if (invalid.any) return this.invalid(invalid);

    const scoped = this.rows.filter((r) => r.tenantId === caller.tenant);
    const guard = this.guard(request.filters, request.search, this.knownCount(scoped));
    if (guard.refused.length) return this.tooBroad(guard.refused);

    const matched = this.apply(scoped, request.filters, request.search, request.timeZone);
    const sorted = this.sort(
      matched,
      request.sort.length ? request.sort : this.columns.defaultSort,
    );
    const start = request.page * request.pageSize;
    const rows = sorted.slice(start, start + request.pageSize).map((r) => this.project(r, caller));
    return {
      status: 200,
      body: {
        rows,
        totalCount: request.includeTotal === false ? null : matched.length,
        capabilities: guard.capabilities,
      },
    };
  }

  values(request: GridValuesRequest, caller: MockCaller): MockResponse {
    const invalid = new Invalid();
    const column = this.column(request.field, caller);
    if (!column) invalid.add(this.key(request.field), 'unknownField', 'Unknown field.');
    else if (column.type !== 'set' || !column.filterable)
      invalid.add(request.field, 'notSetColumn', 'Not a set column.');
    const others = { ...(request.filters ?? {}) };
    delete others[request.field];
    this.validateFilters(others, caller, invalid, request.timeZone);
    if (request.search !== undefined && request.search.length > GRID_LIMITS.maxSearchLength) {
      invalid.add('search', 'tooLong', 'Search is too long.');
    }
    if (invalid.any) return this.invalid(invalid);

    const scoped = this.rows.filter((r) => r.tenantId === caller.tenant);
    const guard = this.guard(others, request.search, this.knownCount(scoped));
    if (guard.refused.length) return this.tooBroad(guard.refused);

    const prop = PROPERTY[request.field];
    const distinct = new Set<string>();
    for (const r of this.apply(scoped, others, request.search, request.timeZone)) {
      const v = r[prop];
      if (v !== null && v !== undefined) distinct.add(String(v));
    }
    const values = [...distinct].sort();
    return {
      status: 200,
      body: {
        values: values.slice(0, GRID_LIMITS.maxDistinctValues),
        truncated: values.length > GRID_LIMITS.maxDistinctValues,
      },
    };
  }

  private knownCount(scoped: Row[]): number | undefined {
    return this.options.knowsScopedSize ? scoped.length : undefined;
  }

  // ---- validation -------------------------------------------------------------------------

  /** A restricted column is unknown unless the caller is finance (fail-closed). */
  private column(field: string, caller: MockCaller) {
    const c = this.columns.byField.get(field);
    if (!c || (c.restricted && !caller.finance)) return undefined;
    return c;
  }

  /** Error keys are allow-listed ids or `(invalid field)`: a raw key is never echoed. */
  private key(field: string): string {
    return GRID_FIELD_PATTERN.test(field) ? field : '(invalid field)';
  }

  private validateShape(request: GridRequest, invalid: Invalid): void {
    if (!Number.isInteger(request.page) || request.page < 0)
      invalid.add('page', 'outOfRange', 'Page must be 0 or more.');
    if (
      !Number.isInteger(request.pageSize) ||
      request.pageSize < 1 ||
      request.pageSize > GRID_LIMITS.maxPageSize
    ) {
      invalid.add('pageSize', 'outOfRange', `Page size must be 1 to ${GRID_LIMITS.maxPageSize}.`);
    }
    if ((request.sort ?? []).length > GRID_LIMITS.maxSorts)
      invalid.add('sort', 'tooMany', 'At most 3 sorts.');
    const fields = (request.sort ?? []).map((s) => s.field);
    if (new Set(fields).size !== fields.length)
      invalid.add('sort', 'duplicate', 'A field is sorted twice.');
    if (Object.keys(request.filters ?? {}).length > GRID_LIMITS.maxFilters)
      invalid.add('filters', 'tooMany', 'Too many filters.');
    if (request.search !== undefined && request.search.length > GRID_LIMITS.maxSearchLength) {
      invalid.add('search', 'tooLong', 'Search is too long.');
    }
  }

  private validateFilters(
    filters: Record<string, GridFilter>,
    caller: MockCaller,
    invalid: Invalid,
    timeZone: string | undefined,
  ): void {
    for (const [field, f] of Object.entries(filters)) {
      const c = this.column(field, caller);
      if (!c) {
        invalid.add(this.key(field), 'unknownField', 'Unknown field.');
        continue;
      }
      if (!c.filterable) invalid.add(field, 'notFilterable', 'This field cannot be filtered.');
      else if (f.type !== c.type)
        invalid.add(field, 'typeMismatch', 'Filter type does not match the column.');
      else if (!c.ops.includes(f.op))
        invalid.add(field, 'unsupportedOperator', 'Operator not allowed.');
      else if (c.type === 'set') {
        if (!Array.isArray(f.values)) invalid.add(field, 'valueRequired', 'Values are required.');
        else if (f.values.length > GRID_LIMITS.maxSetValues)
          invalid.add(field, 'tooMany', 'Too many values.');
      } else if (!VALUELESS_OPS.includes(f.op) && (f.value === undefined || f.value === null)) {
        invalid.add(field, 'valueRequired', 'A value is required.');
      } else if (c.type === 'date' && !VALUELESS_OPS.includes(f.op)) {
        for (const v of [f.value, f.op === 'inRange' ? f.valueTo : undefined]) {
          if (v === undefined && f.op !== 'inRange') continue;
          if (typeof v !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(v) || v < GRID_LIMITS.minDate) {
            invalid.add(field, 'invalidValue', 'Date must be YYYY-MM-DD, 1753-01-01 or later.');
          }
        }
        if (c.dateSemantics === 'local') {
          if (!timeZone) invalid.add('timeZone', 'timeZoneRequired', 'A time zone is required.');
          else if (!isTimeZone(timeZone))
            invalid.add('timeZone', 'invalidTimeZone', 'Unknown time zone.');
        }
      } else if (
        c.type === 'text' &&
        typeof f.value === 'string' &&
        f.value.length > GRID_LIMITS.maxTextValueLength
      ) {
        invalid.add(field, 'tooLong', 'Value is too long.');
      }
    }
  }

  private guard(
    filters: Record<string, GridFilter>,
    search: string | undefined,
    knownRowCount: number | undefined,
  ) {
    const threshold = this.columns.largeDatasetThreshold;
    if (threshold === undefined) {
      return { refused: [] as string[], capabilities: { textFilters: 'enabled' as const } };
    }
    // A guarded grid always reports its threshold; the reason only while disabled.
    if (knownRowCount !== undefined && knownRowCount <= threshold) {
      return {
        refused: [] as string[],
        capabilities: { textFilters: 'enabled' as const, threshold },
      };
    }
    const narrowed = Object.entries(filters).some(
      ([field, f]) => this.columns.byField.get(field)?.indexed && !NON_SELECTIVE_OPS.includes(f.op),
    );
    const capabilities = narrowed
      ? { textFilters: 'enabled' as const, threshold }
      : { textFilters: 'disabled' as const, reason: 'resultSetTooLarge' as const, threshold };
    if (narrowed) return { refused: [] as string[], capabilities };
    const refused = Object.entries(filters)
      .filter(([, f]) => f.type === 'text' && SCAN_OPS.includes(f.op as never))
      .map(([field]) => field);
    if (search?.trim()) refused.push('search');
    return { refused, capabilities };
  }

  // ---- evaluation -------------------------------------------------------------------------

  private apply(
    rows: Row[],
    filters: Record<string, GridFilter>,
    search: string | undefined,
    timeZone?: string,
  ): Row[] {
    const predicates = Object.entries(filters).map(([field, f]) =>
      this.predicate(field, f, timeZone),
    );
    const term = search?.trim().toLowerCase();
    const searchable = this.columns.columns
      .filter((c) => c.searchable)
      .map((c) => PROPERTY[c.field]);
    return rows.filter(
      (r) =>
        predicates.every((p) => p(r)) &&
        (!term ||
          searchable.some((p) =>
            String(r[p] ?? '')
              .toLowerCase()
              .includes(term),
          )),
    );
  }

  private predicate(field: string, f: GridFilter, timeZone?: string): (r: Row) => boolean {
    const c = this.columns.byField.get(field)!;
    const prop = PROPERTY[field];
    const blank = (v: unknown) => v === null || v === undefined || v === '';
    if (f.op === 'blank') return (r) => blank(r[prop]);
    if (f.op === 'notBlank') return (r) => !blank(r[prop]);
    switch (c.type) {
      case 'text': {
        const needle = String(f.value).toLowerCase();
        const text = (r: Row) => String(r[prop] ?? '').toLowerCase();
        switch (f.op) {
          case 'contains':
            return (r) => text(r).includes(needle);
          // Negative text operators include blanks.
          case 'notContains':
            return (r) => !text(r).includes(needle);
          case 'equals':
            return (r) => text(r) === needle;
          case 'notEqual':
            return (r) => text(r) !== needle;
          case 'startsWith':
            return (r) => text(r).startsWith(needle);
          case 'endsWith':
            return (r) => text(r).endsWith(needle);
        }
        break;
      }
      case 'number':
        return numeric((r) => r[prop] as number | null, f, Number(f.value), Number(f.valueTo));
      case 'date': {
        const tz = c.dateSemantics === 'local' ? (timeZone ?? 'UTC') : 'UTC';
        const start = (day: string) => startOfDay(day, tz);
        const next = (day: string) => startOfDay(addDay(day), tz);
        const value = (r: Row) => (r[prop] ? Date.parse(r[prop] as string) : null);
        const a = String(f.value);
        switch (f.op) {
          case 'equals':
            return (r) => between(value(r), start(a), next(a));
          case 'notEqual':
            return (r) => value(r) !== null && !between(value(r), start(a), next(a));
          case 'lessThan':
            return (r) => value(r) !== null && value(r)! < start(a);
          case 'lessThanOrEqual':
            return (r) => value(r) !== null && value(r)! < next(a);
          case 'greaterThan':
            return (r) => value(r) !== null && value(r)! >= next(a);
          case 'greaterThanOrEqual':
            return (r) => value(r) !== null && value(r)! >= start(a);
          case 'inRange':
            return (r) => between(value(r), start(a), next(String(f.valueTo)));
        }
        break;
      }
      case 'set': {
        const allowed = new Set(f.values ?? []);
        return (r) => allowed.has(String(r[prop]));
      }
      case 'boolean':
        return (r) => r[prop] === f.value;
    }
    return () => false;
  }

  private sort(rows: Row[], sort: { field: string; dir: 'asc' | 'desc' }[]): Row[] {
    const keys = [...sort, { field: 'shipmentId', dir: 'asc' as const }];
    return [...rows].sort((x, y) => {
      for (const k of keys) {
        const prop = PROPERTY[k.field];
        const a = x[prop];
        const b = y[prop];
        if (a === b) continue;
        // Nulls first ascending, last descending.
        const cmp = a === null ? -1 : b === null ? 1 : a < b ? -1 : 1;
        return k.dir === 'asc' ? cmp : -cmp;
      }
      return 0;
    });
  }

  private project(row: Row, caller: MockCaller): ShipmentRow {
    const { tenantId: _tenant, ...rest } = row;
    return { ...rest, priceTier: caller.finance ? row.priceTier : null };
  }

  private invalid(invalid: Invalid): MockResponse {
    return {
      status: 400,
      body: {
        type: 'grid-request-invalid',
        title: 'The grid request is invalid.',
        status: 400,
        errors: invalid.errors,
        errorCodes: invalid.codes,
        traceId: traceId(),
      },
    };
  }

  private tooBroad(fields: string[]): MockResponse {
    return {
      status: 400,
      body: {
        type: 'grid-filter-too-broad',
        title: 'The filter is too broad for this grid.',
        status: 400,
        fields,
        threshold: this.columns.largeDatasetThreshold,
        traceId: traceId(),
      },
    };
  }
}

function numeric(
  get: (r: Row) => number | null,
  f: GridFilter,
  a: number,
  b: number,
): (r: Row) => boolean {
  const has = (v: number | null): v is number => v !== null && v !== undefined;
  switch (f.op) {
    case 'equals':
      return (r) => get(r) === a;
    // Number notEqual excludes nulls.
    case 'notEqual':
      return (r) => has(get(r)) && get(r) !== a;
    case 'lessThan':
      return (r) => has(get(r)) && get(r)! < a;
    case 'lessThanOrEqual':
      return (r) => has(get(r)) && get(r)! <= a;
    case 'greaterThan':
      return (r) => has(get(r)) && get(r)! > a;
    case 'greaterThanOrEqual':
      return (r) => has(get(r)) && get(r)! >= a;
    case 'inRange':
      return (r) => has(get(r)) && get(r)! >= a && get(r)! <= b;
    default:
      return () => false;
  }
}

function between(v: number | null, from: number, to: number): boolean {
  return v !== null && v >= from && v < to;
}

function addDay(day: string): string {
  const [y, m, d] = day.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + 1)).toISOString().slice(0, 10);
}

/** UTC instant of local midnight of `day` in `timeZone` (DST-safe: offset taken at that instant). */
export function startOfDay(day: string, timeZone: string): number {
  const [y, m, d] = day.split('-').map(Number);
  const guess = Date.UTC(y, m - 1, d);
  let instant = guess - offsetMs(guess, timeZone);
  instant = guess - offsetMs(instant, timeZone);
  return instant;
}

function offsetMs(instant: number, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(new Date(instant));
  const get = (type: string) => Number(parts.find((p) => p.type === type)?.value);
  const asUtc = Date.UTC(
    get('year'),
    get('month') - 1,
    get('day'),
    get('hour'),
    get('minute'),
    get('second'),
  );
  return asUtc - Math.floor(instant / 1000) * 1000;
}

function isTimeZone(timeZone: string): boolean {
  try {
    new Intl.DateTimeFormat('en-US', { timeZone });
    return true;
  } catch {
    return false;
  }
}

function traceId(): string {
  return `demo-${Math.random().toString(16).slice(2, 10)}`;
}
