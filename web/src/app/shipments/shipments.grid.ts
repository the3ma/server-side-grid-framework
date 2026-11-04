import { defineGridColumns, gridColumn, withoutRestricted } from '../grid/grid-columns';
import type { ShipmentRow } from './shipment-row.schema';

/** Mirrors the server's column map for `shipments` (docs/CONTRACT.md). */
export const SHIPMENTS_GRID = defineGridColumns<ShipmentRow>({
  grid: 'shipments',
  largeDatasetThreshold: 50_000,
  defaultSort: [{ field: 'shippedOn', dir: 'desc' }],
  columns: [
    gridColumn({
      field: 'shipmentId',
      type: 'number',
      headerName: 'Shipment ID',
      sortable: true,
      filterable: true,
      indexed: true,
      colDef: { width: 130 },
    }),
    gridColumn({
      field: 'trackingNo',
      type: 'text',
      headerName: 'Tracking no.',
      sortable: true,
      filterable: true,
      searchable: true,
      indexed: true,
    }),
    gridColumn({
      field: 'warehouseCode',
      type: 'set',
      headerName: 'Warehouse',
      sortable: true,
      filterable: true,
      indexed: true,
      colDef: { width: 120 },
    }),
    gridColumn({
      field: 'destination',
      type: 'text',
      headerName: 'Destination',
      sortable: true,
      filterable: true,
      searchable: true,
      colDef: { minWidth: 200 },
    }),
    gridColumn({
      field: 'status',
      type: 'set',
      headerName: 'Status',
      sortable: true,
      filterable: true,
      indexed: true,
    }),
    gridColumn({ field: 'carrier', type: 'set', headerName: 'Carrier', filterable: true }),
    gridColumn({
      field: 'weightKg',
      type: 'number',
      headerName: 'Weight (kg)',
      sortable: true,
      filterable: true,
      colDef: { width: 140 },
    }),
    gridColumn({
      field: 'shippedOn',
      type: 'date',
      headerName: 'Shipped',
      sortable: true,
      filterable: true,
      dateSemantics: 'local',
      colDef: { minWidth: 170 },
    }),
    gridColumn({
      field: 'deliveredOn',
      type: 'date',
      headerName: 'Delivered (UTC)',
      sortable: true,
      filterable: true,
      dateSemantics: 'utc',
      colDef: { minWidth: 170 },
    }),
    gridColumn({
      field: 'flagged',
      type: 'boolean',
      headerName: 'Flagged',
      filterable: true,
      colDef: { width: 120 },
    }),
    gridColumn({
      field: 'priceTier',
      type: 'set',
      headerName: 'Price tier',
      filterable: true,
      restricted: true,
    }),
  ],
});

/** The columns this caller may use: restricted columns only for a caller with the finance role. */
export function shipmentsColumns(finance: boolean) {
  return finance ? SHIPMENTS_GRID : withoutRestricted(SHIPMENTS_GRID);
}
