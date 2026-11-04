import type { GridRequest, GridValuesRequest } from './grid-request.types';

/**
 * Value-free summary of a request for logs: grid id, field ids, operators and counts only.
 * Filter values and the search text never reach a log.
 */
export function describeGridRequest(
  grid: string,
  request: GridRequest | GridValuesRequest,
): string {
  const filters = Object.entries(request.filters)
    .map(([field, f]) => `${field}:${f.op}${f.values ? `(${f.values.length})` : ''}`)
    .join(',');
  const paging =
    'page' in request
      ? ` page=${request.page} size=${request.pageSize}`
      : ` values=${request.field}`;
  const sort =
    'sort' in request ? ` sort=${request.sort.map((s) => `${s.field}:${s.dir}`).join(',')}` : '';
  return `grid=${grid}${paging}${sort} filters=[${filters}] search=${request.search ? 'yes' : 'no'}`;
}
