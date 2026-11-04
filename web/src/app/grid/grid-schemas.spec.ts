import { describe, expect, it } from 'vitest';
import { ShipmentRowSchema } from '../shipments/shipment-row.schema';
import { GridPageSchema, GridValuesPageSchema } from './grid-schemas';

const row = {
  shipmentId: 1,
  trackingNo: 'TRK-0000001',
  warehouseCode: 'WH-104',
  destination: 'Lakeview Hardware',
  status: 'received',
  carrier: null,
  weightKg: 2.5,
  shippedOn: '2024-05-01T08:00:00Z',
  deliveredOn: null,
  flagged: false,
  priceTier: null,
};
const schema = GridPageSchema(ShipmentRowSchema);
const capabilities = { textFilters: 'enabled' };

describe('GridPageSchema', () => {
  it('parses a page', () => {
    const page = schema.parse({ rows: [row], totalCount: 1, capabilities });
    expect(page.rows[0].flagged).toBe(false);
    expect(
      schema.safeParse({ rows: [{ ...row, flagged: 0 }], totalCount: 1, capabilities }).success,
    ).toBe(false);
    expect(
      schema.safeParse({ rows: [{ ...row, extra: 1 }], totalCount: 1, capabilities }).success,
    ).toBe(false);
  });

  it('accepts totalCount null (not computed) and keeps it null', () => {
    expect(schema.parse({ rows: [], totalCount: null, capabilities }).totalCount).toBeNull();
  });

  it('fails the whole page when one row does not match', () => {
    const bad = { ...row, shipmentId: 'one' };
    expect(schema.safeParse({ rows: [row, bad], totalCount: 2, capabilities }).success).toBe(false);
  });

  it('fails without capabilities or with a negative total', () => {
    expect(schema.safeParse({ rows: [], totalCount: 0 }).success).toBe(false);
    expect(schema.safeParse({ rows: [], totalCount: -1, capabilities }).success).toBe(false);
  });
});

describe('GridValuesPageSchema', () => {
  it('parses values and the truncated flag', () => {
    expect(GridValuesPageSchema.parse({ values: ['a', 'b'], truncated: false }).values).toEqual([
      'a',
      'b',
    ]);
    expect(GridValuesPageSchema.safeParse({ values: ['a'] }).success).toBe(false);
  });
});
