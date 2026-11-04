import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { SHIPMENTS_GRID } from '../shipments/shipments.grid';
import { diffGridManifest, type GridManifest, toGridManifest } from './grid-manifest';

/** Written by the backend test suite from its column map. */
const SERVER_MANIFEST = resolve(__dirname, '../../../../docs/manifest/shipments.json');

describe('toGridManifest', () => {
  it('describes the declaration without SQL', () => {
    const manifest = toGridManifest(SHIPMENTS_GRID);
    expect(manifest.grid).toBe('shipments');
    expect(manifest.threshold).toBe(50000);
    expect(manifest.defaultSort).toEqual([{ field: 'shippedOn', dir: 'desc' }]);
    expect(manifest.columns.find((c) => c.field === 'priceTier')).toMatchObject({
      restricted: true,
      ops: ['in'],
    });
    expect(manifest.columns.find((c) => c.field === 'shippedOn')?.dateSemantics).toBe('local');
  });
});

describe('diffGridManifest', () => {
  it('is empty for an identical manifest and names every drift', () => {
    const same = toGridManifest(SHIPMENTS_GRID);
    expect(diffGridManifest(SHIPMENTS_GRID, same)).toEqual([]);

    const drifted: GridManifest = structuredClone(same);
    drifted.threshold = 10000;
    drifted.columns = drifted.columns.filter((c) => c.field !== 'flagged');
    drifted.columns.find((c) => c.field === 'trackingNo')!.indexed = false;
    drifted.columns.push({ ...drifted.columns[0], field: 'extra' });
    expect(diffGridManifest(SHIPMENTS_GRID, drifted)).toEqual([
      'threshold: client 50000 vs server 10000',
      'trackingNo.indexed: client true vs server false',
      'flagged: declared on the client, missing on the server',
      'extra: on the server, not declared on the client',
    ]);
  });
});

describe('contract with the backend column map', () => {
  it('matches docs/manifest/shipments.json, written by the backend tests', () => {
    expect(
      existsSync(SERVER_MANIFEST),
      'run the backend tests first: they write the manifest',
    ).toBe(true);
    const server = JSON.parse(readFileSync(SERVER_MANIFEST, 'utf8')) as GridManifest;
    expect(diffGridManifest(SHIPMENTS_GRID, server)).toEqual([]);
  });
});
