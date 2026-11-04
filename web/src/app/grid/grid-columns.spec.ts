import { describe, expect, it } from 'vitest';
import { SHIPMENTS_GRID, shipmentsColumns } from '../shipments/shipments.grid';
import { defineGridColumns, gridColumn, SET_FILTER_NAME, toColDefs } from './grid-columns';

const options = { guarded: false, timeZone: 'Europe/Athens' };
const colDef = (field: string, guarded = false) =>
  toColDefs(SHIPMENTS_GRID, { ...options, guarded }).find((c) => c.colId === field)!;

describe('gridColumn', () => {
  it('requires camelCase field ids', () => {
    for (const field of ['RegionId', 'region_id', 'ORDER_NO', '1st']) {
      expect(() => gridColumn({ field, type: 'text', headerName: 'x' })).toThrow(/camelCase/);
    }
    expect(() => gridColumn({ field: 'regionId', type: 'text', headerName: 'x' })).not.toThrow();
  });

  it('refuses operators the type does not have', () => {
    expect(() =>
      gridColumn({ field: 'n', type: 'number', headerName: 'n', ops: ['contains'] }),
    ).toThrow(/not valid/);
  });

  it('requires date semantics on date columns and allows search only on text', () => {
    expect(() => gridColumn({ field: 'd', type: 'date', headerName: 'd' })).toThrow(
      /dateSemantics/,
    );
    expect(() =>
      gridColumn({ field: 'n', type: 'number', headerName: 'n', searchable: true }),
    ).toThrow(/searchable/);
  });

  it('refuses duplicate fields', () => {
    const c = gridColumn({ field: 'a', type: 'text', headerName: 'a' });
    expect(() => defineGridColumns({ grid: 'g', columns: [c, c] })).toThrow(/duplicate/);
  });
});

describe('toColDefs', () => {
  it('allows exactly the declared operators, one condition each', () => {
    const weight = colDef('weightKg');
    expect(weight.filter).toBe('agNumberColumnFilter');
    expect(weight.filterParams).toMatchObject({ maxNumConditions: 1, inRangeInclusive: true });
    expect(weight.filterParams.filterOptions).toContain('inRange');
  });

  it('removes the guarded scan operators from text columns while guarded', () => {
    expect(colDef('destination').filterParams.filterOptions).toContain('contains');
    const guarded = colDef('destination', true).filterParams.filterOptions as string[];
    expect(guarded).not.toContain('contains');
    expect(guarded).not.toContain('notContains');
    expect(guarded).not.toContain('endsWith');
    expect(guarded).toContain('startsWith');
  });

  it('labels the time zone of each date filter', () => {
    expect(colDef('shippedOn').headerTooltip).toBe(
      'Date filter uses your time zone (Europe/Athens)',
    );
    expect(colDef('deliveredOn').headerTooltip).toBe('Date filter uses UTC days');
  });

  it('uses the set filter for set and boolean columns, and no filter on non-filterable columns', () => {
    expect(colDef('status').filter).toBe(SET_FILTER_NAME);
    expect(colDef('flagged').filterParams).toMatchObject({ kind: 'boolean' });
    const plain = toColDefs(
      defineGridColumns({
        grid: 'g',
        columns: [gridColumn({ field: 'a', type: 'text', headerName: 'a' })],
      }),
      options,
    )[0];
    expect(plain.filter).toBe(false);
    expect(plain.sortable).toBe(false);
  });

  it('declares restricted columns only for an finance caller', () => {
    expect(shipmentsColumns(false).byField.has('priceTier')).toBe(false);
    expect(shipmentsColumns(true).byField.has('priceTier')).toBe(true);
  });
});
