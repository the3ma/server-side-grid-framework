import { describe, expect, it } from 'vitest';
import type { GridRequest } from '../grid/grid-request.types';
import { describeGridRequest } from '../grid/grid-log';
import { generateRows, MockGridBackend, startOfDay } from './mock-grid-backend';

// Tenant 1 is above the 50 000 guard threshold, tenant 2 below it.
const rows = generateRows({ 1: 60_000, 2: 5_000 });
const backend = new MockGridBackend(rows);
/** A backend that knows the scoped size (the API's KnownRowCount) lifts the guard for small scopes. */
const sizeAware = new MockGridBackend(rows, { knowsScopedSize: true });
const big = { tenant: 1, finance: false };
const small = { tenant: 2, finance: false };

function request(overrides: Partial<GridRequest> = {}): GridRequest {
  return { page: 0, pageSize: 100, sort: [], filters: {}, includeTotal: true, ...overrides };
}

type Body = Record<string, unknown> & {
  rows: Record<string, unknown>[];
  totalCount: number | null;
};

describe('MockGridBackend', () => {
  it('scopes rows to the caller and reports the guard state', () => {
    const page = backend.page(request(), big);
    expect(page.status).toBe(200);
    const body = page.body as Body;
    expect(body.totalCount).toBe(60_000);
    expect(body['capabilities']).toEqual({
      textFilters: 'disabled',
      reason: 'resultSetTooLarge',
      threshold: 50000,
    });
    expect((backend.page(request(), small).body as Body)['capabilities']).toMatchObject({
      textFilters: 'disabled',
    });
    expect((sizeAware.page(request(), small).body as Body)['capabilities']).toEqual({
      textFilters: 'enabled',
      threshold: 50000,
    });
  });

  it('refuses a scan filter and the search on a large grid until an indexed filter narrows it', () => {
    const scan = backend.page(
      request({
        filters: { destination: { type: 'text', op: 'contains', value: 'lake' } },
        search: 'trk',
      }),
      big,
    );
    expect(scan.status).toBe(400);
    expect(scan.body).toMatchObject({
      type: 'grid-filter-too-broad',
      fields: ['destination', 'search'],
      threshold: 50000,
    });

    const narrowed = backend.page(
      request({
        filters: {
          destination: { type: 'text', op: 'contains', value: 'lake' },
          status: { type: 'set', op: 'in', values: ['received'] },
        },
      }),
      big,
    );
    expect(narrowed.status).toBe(200);
  });

  it('returns totalCount null, never 0, when no total was asked for', () => {
    expect(
      (backend.page(request({ page: 1, includeTotal: false }), big).body as Body).totalCount,
    ).toBeNull();
  });

  it('treats a restricted column as unknown without the finance role and redacts malformed keys', () => {
    const withoutRole = backend.page(
      request({ filters: { priceTier: { type: 'set', op: 'in', values: ['contract'] } } }),
      big,
    );
    expect(withoutRole.body).toMatchObject({ errorCodes: { priceTier: ['unknownField'] } });

    const malformed = backend.page(
      request({ filters: { 'DROP TABLE': { type: 'text', op: 'equals', value: 'x' } } }),
      big,
    );
    expect(JSON.stringify(malformed.body)).not.toContain('DROP');
    expect(malformed.body).toMatchObject({ errorCodes: { '(invalid field)': ['unknownField'] } });

    const finance = backend.page(
      request({ filters: { priceTier: { type: 'set', op: 'in', values: ['contract'] } } }),
      { tenant: 1, finance: true },
    );
    expect(finance.status).toBe(200);
    expect((finance.body as Body).rows[0]['priceTier']).toBe('contract');
  });

  it('validates limits without echoing values', () => {
    const bad = backend.page(request({ pageSize: 501, page: -1 }), small);
    expect(bad.body).toMatchObject({
      errorCodes: { page: ['outOfRange'], pageSize: ['outOfRange'] },
    });
    const date = backend.page(
      request({ filters: { deliveredOn: { type: 'date', op: 'equals', value: '1700-01-01' } } }),
      small,
    );
    expect(JSON.stringify(date.body)).not.toContain('1700');
  });

  it('requires a time zone for local date filters', () => {
    const missing = backend.page(
      request({ filters: { shippedOn: { type: 'date', op: 'equals', value: '2024-05-01' } } }),
      small,
    );
    expect(missing.body).toMatchObject({ errorCodes: { timeZone: ['timeZoneRequired'] } });
  });

  it('sorts with the tiebreaker so pages are stable', () => {
    const first = backend.page(request({ sort: [{ field: 'status', dir: 'asc' }] }), small)
      .body as Body;
    const again = backend.page(request({ sort: [{ field: 'status', dir: 'asc' }] }), small)
      .body as Body;
    expect(first.rows.map((r) => r['shipmentId'])).toEqual(again.rows.map((r) => r['shipmentId']));
  });

  it('lists distinct sorted values and ignores the field’s own filter', () => {
    const values = backend.values(
      { field: 'status', filters: { status: { type: 'set', op: 'in', values: ['received'] } } },
      small,
    );
    expect(values.body).toEqual({
      values: ['cancelled', 'delivered', 'in_transit', 'received', 'returned'],
      truncated: false,
    });
  });
});

describe('startOfDay', () => {
  it('is local midnight in the zone, across a DST change', () => {
    expect(new Date(startOfDay('2024-03-31', 'Europe/Athens')).toISOString()).toBe(
      '2024-03-30T22:00:00.000Z',
    );
    expect(new Date(startOfDay('2024-04-01', 'Europe/Athens')).toISOString()).toBe(
      '2024-03-31T21:00:00.000Z',
    );
    expect(new Date(startOfDay('2024-04-01', 'UTC')).toISOString()).toBe(
      '2024-04-01T00:00:00.000Z',
    );
  });
});

describe('describeGridRequest', () => {
  it('logs field ids, operators and counts only - never values or the search text', () => {
    const line = describeGridRequest(
      'shipments',
      request({
        filters: {
          trackingNo: { type: 'text', op: 'startsWith', value: 'TRK-00042' },
          status: { type: 'set', op: 'in', values: ['received', 'returned'] },
        },
        search: 'Lakeview',
      }),
    );
    expect(line).toBe(
      'grid=shipments page=0 size=100 sort= filters=[trackingNo:startsWith,status:in(2)] search=yes',
    );
    expect(line).not.toContain('TRK-00042');
    expect(line).not.toContain('Lakeview');
  });
});
