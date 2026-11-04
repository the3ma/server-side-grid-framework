import { GridProblemSchema } from './grid-schemas';

/** Thrown when the grid state cannot be expressed in the contract. Fails loudly, never guesses. */
export class GridRequestMappingError extends Error {
  constructor(
    readonly code: 'multipleConditions' | 'unknownField' | 'unsupportedFilter' | 'unexpressible',
    readonly field: string,
  ) {
    // Field ids are allow-listed column ids, never user input values.
    super(`Grid filter on "${field}" cannot be sent: ${code}`);
    this.name = 'GridRequestMappingError';
  }
}

export type GridErrorKind =
  | 'invalid'
  | 'tooBroad'
  | 'timeout'
  | 'badRequest'
  | 'forbidden'
  | 'network'
  | 'http'
  | 'response'
  | 'mapping';

export interface GridErrorDetails {
  /** Field ids the server rejected (invalid) or refused (tooBroad; `search` = quick search). */
  fields: string[];
  threshold?: number;
  timeoutSeconds?: number;
  traceId?: string;
  status?: number;
}

/** A failed grid request, typed so the UI can explain it. Messages never contain values. */
export class GridRequestError extends Error {
  constructor(
    readonly kind: GridErrorKind,
    readonly details: GridErrorDetails = { fields: [] },
  ) {
    super(`Grid request failed: ${kind}`);
    this.name = 'GridRequestError';
  }
}

/**
 * Maps an HTTP failure (status + parsed body) to a GridRequestError.
 * Pure, so it is tested without Angular; status 0 means the request never reached the server.
 */
export function toGridRequestError(status: number, body: unknown): GridRequestError {
  if (status === 0) {
    return new GridRequestError('network', { fields: [], status });
  }
  const problem = GridProblemSchema.safeParse(body);
  if (problem.success) {
    const p = problem.data;
    const traceId = p.traceId ?? undefined;
    switch (p.type) {
      case 'grid-request-invalid':
        return new GridRequestError('invalid', {
          fields: Object.keys({ ...p.errors, ...p.errorCodes }),
          traceId,
          status,
        });
      case 'grid-filter-too-broad':
        return new GridRequestError('tooBroad', {
          fields: p.fields,
          threshold: p.threshold,
          traceId,
          status,
        });
      case 'grid-query-timeout':
        return new GridRequestError('timeout', {
          fields: [],
          timeoutSeconds: p.timeoutSeconds,
          traceId,
          status,
        });
      case 'grid-query-failed':
        return new GridRequestError('http', { fields: [], traceId, status });
    }
  }
  if (status === 400) return new GridRequestError('badRequest', { fields: [], status });
  if (status === 401 || status === 403)
    return new GridRequestError('forbidden', { fields: [], status });
  return new GridRequestError('http', { fields: [], status });
}

/** User-facing text for an error. `label` turns a field id into its column header. */
export function gridErrorMessage(
  error: GridRequestError,
  label: (field: string) => string = (f) => f,
): string {
  const names = error.details.fields.map((f) => (f === 'search' ? 'Search' : label(f)));
  switch (error.kind) {
    case 'invalid':
      return names.length
        ? `The server could not apply: ${names.join(', ')}. Change or clear these, then try again.`
        : 'The server could not apply this request. Change the filters, then try again.';
    case 'tooBroad':
      return `This grid can hold more than ${formatCount(error.details.threshold)} rows, so it cannot be searched this way yet. Clear the refused filters (${names.join(', ')}) or first filter on an indexed column.`;
    case 'timeout':
      return `The query took longer than ${error.details.timeoutSeconds ?? '?'} seconds. Narrow the filters, then try again.`;
    case 'badRequest':
      return 'The request was not accepted. Try again.';
    case 'forbidden':
      return 'You do not have access to this data.';
    case 'network':
      return 'The server could not be reached. Check your connection, then try again.';
    case 'response':
      return 'The server sent rows this page cannot read. Try again; if it persists, report it.';
    case 'mapping':
      return 'This combination of filters cannot be sent. Use one condition per column.';
    case 'http':
    default:
      return 'The rows could not be loaded. Try again.';
  }
}

function formatCount(n: number | undefined): string {
  return n === undefined ? 'the limit' : n.toLocaleString('en-US');
}
