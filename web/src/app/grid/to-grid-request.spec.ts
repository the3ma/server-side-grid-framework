import { describe, expect, it } from 'vitest';
import { shipmentsColumns, SHIPMENTS_GRID } from '../shipments/shipments.grid';
import { GridRequestMappingError } from './grid-errors';
import {
  type AgFilterModel,
  type GridStateInput,
  isGridNarrowed,
  toGridRequest,
  toGridValuesRequest,
} from './to-grid-request';

const TZ = 'Europe/Athens';

function state(overrides: Partial<GridStateInput> = {}): GridStateInput {
  return {
    startRow: 0,
    pageSize: 100,
    sortModel: [],
    filterModel: {},
    includeTotal: true,
    timeZone: TZ,
    ...overrides,
  };
}

function mappingError(fn: () => unknown): GridRequestMappingError {
  try {
    fn();
  } catch (err) {
    if (err instanceof GridRequestMappingError) return err;
    throw err;
  }
  throw new Error('expected a GridRequestMappingError');
}

describe('toGridRequest', () => {
  it('maps the block start to a 0-based page', () => {
    expect(toGridRequest(SHIPMENTS_GRID, state({ startRow: 300 })).page).toBe(3);
    expect(toGridRequest(SHIPMENTS_GRID, state()).page).toBe(0);
  });

  it('keeps sort order and leaves the tiebreaker to the server', () => {
    const request = toGridRequest(
      SHIPMENTS_GRID,
      state({
        sortModel: [
          { colId: 'status', sort: 'asc' },
          { colId: 'shippedOn', sort: 'desc' },
        ],
      }),
    );
    expect(request.sort).toEqual([
      { field: 'status', dir: 'asc' },
      { field: 'shippedOn', dir: 'desc' },
    ]);
  });

  it('refuses more than three sorts and unsortable columns', () => {
    const four = ['shipmentId', 'trackingNo', 'status', 'weightKg'].map((colId) => ({
      colId,
      sort: 'asc' as const,
    }));
    expect(
      mappingError(() => toGridRequest(SHIPMENTS_GRID, state({ sortModel: four }))).field,
    ).toBe('sort');
    expect(
      mappingError(() =>
        toGridRequest(SHIPMENTS_GRID, state({ sortModel: [{ colId: 'carrier', sort: 'asc' }] })),
      ).code,
    ).toBe('unknownField');
  });

  it('maps a text filter and fails loudly on two conditions', () => {
    const filterModel: AgFilterModel = {
      destination: { filterType: 'text', type: 'startsWith', filter: 'Lake' },
    };
    expect(toGridRequest(SHIPMENTS_GRID, state({ filterModel })).filters).toEqual({
      destination: { type: 'text', op: 'startsWith', value: 'Lake' },
    });
    const two: AgFilterModel = {
      destination: { filterType: 'text', operator: 'OR', conditions: [{}, {}] },
    };
    expect(
      mappingError(() => toGridRequest(SHIPMENTS_GRID, state({ filterModel: two }))).code,
    ).toBe('multipleConditions');
  });

  it('refuses an operator the column does not allow', () => {
    const filterModel: AgFilterModel = {
      weightKg: { filterType: 'number', type: 'contains', filter: 1 },
    };
    expect(mappingError(() => toGridRequest(SHIPMENTS_GRID, state({ filterModel }))).code).toBe(
      'unsupportedFilter',
    );
  });

  it('maps number ranges with valueTo', () => {
    const filterModel: AgFilterModel = {
      weightKg: { filterType: 'number', type: 'inRange', filter: 1.5, filterTo: 4 },
    };
    expect(toGridRequest(SHIPMENTS_GRID, state({ filterModel })).filters['weightKg']).toEqual({
      type: 'number',
      op: 'inRange',
      value: 1.5,
      valueTo: 4,
    });
  });

  it('sends dates as YYYY-MM-DD text, without any time-zone shift', () => {
    const filterModel: AgFilterModel = {
      shippedOn: {
        filterType: 'date',
        type: 'inRange',
        dateFrom: '2024-03-31 00:00:00',
        dateTo: '2024-04-01 00:00:00',
      },
    };
    const request = toGridRequest(SHIPMENTS_GRID, state({ filterModel }));
    expect(request.filters['shippedOn']).toEqual({
      type: 'date',
      op: 'inRange',
      value: '2024-03-31',
      valueTo: '2024-04-01',
    });
  });

  it('sends timeZone only when a local date column is filtered', () => {
    const local: AgFilterModel = {
      shippedOn: { filterType: 'date', type: 'equals', dateFrom: '2024-05-01 00:00:00' },
    };
    const utc: AgFilterModel = {
      deliveredOn: { filterType: 'date', type: 'equals', dateFrom: '2024-05-01 00:00:00' },
    };
    const blank: AgFilterModel = { shippedOn: { filterType: 'date', type: 'blank' } };
    expect(toGridRequest(SHIPMENTS_GRID, state({ filterModel: local })).timeZone).toBe(TZ);
    expect(toGridRequest(SHIPMENTS_GRID, state({ filterModel: utc })).timeZone).toBeUndefined();
    expect(toGridRequest(SHIPMENTS_GRID, state({ filterModel: blank })).timeZone).toBeUndefined();
    expect(toGridRequest(SHIPMENTS_GRID, state()).timeZone).toBeUndefined();
  });

  it('sends an empty set selection as `in []`, which matches nothing', () => {
    const filterModel: AgFilterModel = { status: { filterType: 'set', values: [] } };
    expect(toGridRequest(SHIPMENTS_GRID, state({ filterModel })).filters['status']).toEqual({
      type: 'set',
      op: 'in',
      values: [],
    });
  });

  describe('boolean columns (set filter over true / false / blank)', () => {
    const map = (values: string[], includeBlank: boolean) =>
      toGridRequest(
        SHIPMENTS_GRID,
        state({ filterModel: { flagged: { filterType: 'set', values, includeBlank } } }),
      ).filters['flagged'];

    it('maps one value to equals', () => {
      expect(map(['true'], false)).toEqual({ type: 'boolean', op: 'equals', value: true });
      expect(map(['false'], false)).toEqual({ type: 'boolean', op: 'equals', value: false });
    });
    it('maps true + false to notBlank and blank only to blank', () => {
      expect(map(['true', 'false'], false)).toEqual({ type: 'boolean', op: 'notBlank' });
      expect(map([], true)).toEqual({ type: 'boolean', op: 'blank' });
    });
    it('sends no filter when everything is selected', () => {
      expect(map(['true', 'false'], true)).toBeUndefined();
    });
    it('fails loudly when one condition cannot express the selection', () => {
      expect(() => map([], false)).toThrow(GridRequestMappingError);
      expect(() => map(['true'], true)).toThrow(GridRequestMappingError);
    });
  });

  it('refuses unknown, non-filterable and (without the finance role) restricted columns', () => {
    const withoutRole = shipmentsColumns(false);
    for (const field of ['nope', 'priceTier']) {
      const filterModel: AgFilterModel = { [field]: { filterType: 'set', values: ['contract'] } };
      expect(mappingError(() => toGridRequest(withoutRole, state({ filterModel }))).code).toBe(
        'unknownField',
      );
    }
  });

  it('trims the search and leaves it out when empty', () => {
    expect(toGridRequest(SHIPMENTS_GRID, state({ search: '  trk-0001 ' })).search).toBe('trk-0001');
    expect('search' in toGridRequest(SHIPMENTS_GRID, state({ search: '   ' }))).toBe(false);
  });
});

describe('toGridValuesRequest', () => {
  it('ignores the field’s own filter', () => {
    const filterModel: AgFilterModel = {
      status: { filterType: 'set', values: ['received'] },
      warehouseCode: { filterType: 'set', values: ['WH-104'] },
    };
    const request = toGridValuesRequest(SHIPMENTS_GRID, 'status', filterModel, undefined, TZ);
    expect(Object.keys(request.filters)).toEqual(['warehouseCode']);
  });

  it('keeps search and scan filters while another indexed filter narrows the grid', () => {
    const filterModel: AgFilterModel = {
      warehouseCode: { filterType: 'set', values: ['WH-104'] },
      destination: { filterType: 'text', type: 'contains', filter: 'lake' },
    };
    const request = toGridValuesRequest(SHIPMENTS_GRID, 'status', filterModel, 'trk', TZ);
    expect(request.search).toBe('trk');
    expect(request.filters['destination']).toBeDefined();
  });

  it('drops search and scan filters when the field’s own filter was the only narrowing one', () => {
    const filterModel: AgFilterModel = {
      status: { filterType: 'set', values: ['received'] },
      destination: { filterType: 'text', type: 'contains', filter: 'lake' },
      weightKg: { filterType: 'number', type: 'greaterThan', filter: 2 },
    };
    const request = toGridValuesRequest(SHIPMENTS_GRID, 'status', filterModel, 'trk', TZ);
    expect(request.search).toBeUndefined();
    expect(Object.keys(request.filters)).toEqual(['weightKg']);
  });
});

describe('isGridNarrowed', () => {
  it('counts only selective operators on indexed columns', () => {
    expect(
      isGridNarrowed(SHIPMENTS_GRID, {
        trackingNo: { type: 'text', op: 'startsWith', value: 'SMP' },
      }),
    ).toBe(true);
    expect(
      isGridNarrowed(SHIPMENTS_GRID, {
        trackingNo: { type: 'text', op: 'contains', value: 'SMP' },
      }),
    ).toBe(false);
    expect(
      isGridNarrowed(SHIPMENTS_GRID, {
        trackingNo: { type: 'text', op: 'notEqual', value: 'SMP' },
      }),
    ).toBe(false);
    expect(
      isGridNarrowed(SHIPMENTS_GRID, { destination: { type: 'text', op: 'equals', value: 'x' } }),
    ).toBe(false);
  });
});
