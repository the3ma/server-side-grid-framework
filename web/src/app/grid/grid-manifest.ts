import type { GridColumns } from './grid-columns';
import type { GridColumnType, GridDateSemantics, GridSort } from './grid-request.types';

/** SQL-free description of a grid, written by the backend from its column map. */
export interface GridManifest {
  grid: string;
  threshold: number | null;
  defaultSort: GridSort[];
  columns: GridManifestColumn[];
}

export interface GridManifestColumn {
  field: string;
  type: GridColumnType;
  sortable: boolean;
  filterable: boolean;
  searchable: boolean;
  indexed: boolean;
  restricted: boolean;
  ops: string[];
  dateSemantics: GridDateSemantics | null;
}

export function toGridManifest<TRow>(columns: GridColumns<TRow>): GridManifest {
  return {
    grid: columns.grid,
    threshold: columns.largeDatasetThreshold ?? null,
    defaultSort: columns.defaultSort,
    columns: columns.columns.map((c) => ({
      field: c.field,
      type: c.type,
      sortable: c.sortable,
      filterable: c.filterable,
      searchable: c.searchable,
      indexed: c.indexed,
      restricted: c.restricted,
      // A column that is not filterable offers no operators, whatever its type allows.
      ops: c.filterable ? [...c.ops] : [],
      dateSemantics: c.dateSemantics ?? null,
    })),
  };
}

/**
 * Differences between the frontend declaration and the backend manifest, as readable lines.
 * An empty list means both sides describe the same grid.
 */
export function diffGridManifest<TRow>(columns: GridColumns<TRow>, server: GridManifest): string[] {
  const client = toGridManifest(columns);
  const diffs: string[] = [];
  if (client.grid !== server.grid)
    diffs.push(`grid: client "${client.grid}" vs server "${server.grid}"`);
  if (client.threshold !== (server.threshold ?? null)) {
    diffs.push(`threshold: client ${client.threshold} vs server ${server.threshold}`);
  }
  if (JSON.stringify(client.defaultSort) !== JSON.stringify(server.defaultSort ?? [])) {
    diffs.push('defaultSort differs');
  }
  const serverColumns = new Map(server.columns.map((c) => [c.field, c]));
  for (const c of client.columns) {
    const s = serverColumns.get(c.field);
    if (!s) {
      diffs.push(`${c.field}: declared on the client, missing on the server`);
      continue;
    }
    for (const key of [
      'type',
      'sortable',
      'filterable',
      'searchable',
      'indexed',
      'restricted',
    ] as const) {
      if (c[key] !== (s[key] ?? false))
        diffs.push(`${c.field}.${key}: client ${c[key]} vs server ${s[key]}`);
    }
    if ((c.dateSemantics ?? null) !== (s.dateSemantics ?? null)) {
      diffs.push(
        `${c.field}.dateSemantics: client ${c.dateSemantics} vs server ${s.dateSemantics}`,
      );
    }
    const clientOps = [...c.ops].sort().join(',');
    const serverOps = [...(s.ops ?? [])].sort().join(',');
    if (clientOps !== serverOps)
      diffs.push(`${c.field}.ops: client [${clientOps}] vs server [${serverOps}]`);
  }
  for (const field of serverColumns.keys()) {
    if (!columns.byField.has(field))
      diffs.push(`${field}: on the server, not declared on the client`);
  }
  return diffs;
}
