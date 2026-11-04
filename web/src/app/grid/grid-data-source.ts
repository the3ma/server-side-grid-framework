import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, defer, map, type Observable, switchMap, tap, throwError } from 'rxjs';
import type { z } from 'zod';
import type { GridColumns } from './grid-columns';
import { GridRequestError, GridRequestMappingError, toGridRequestError } from './grid-errors';
import type {
  GridPage,
  GridRequest,
  GridValuesPage,
  GridValuesRequest,
} from './grid-request.types';
import { describeGridRequest } from './grid-log';
import { GridPageSchema, GridValuesPageSchema } from './grid-schemas';
import {
  type AgFilterModel,
  type GridStateInput,
  toGridRequest,
  toGridValuesRequest,
} from './to-grid-request';

export interface GridDataSource<TRow> {
  readonly url: string;
  readonly columns: GridColumns<TRow>;
  /** Maps the grid state, POSTs it and parses the page strictly. Fails with GridRequestError only. */
  fetchPage(state: GridStateInput): Observable<GridPage<TRow>>;
  /** Distinct values of a set column, scoped like the rows. */
  fetchValues(
    field: string,
    filterModel: AgFilterModel,
    search: string | undefined,
    timeZone: string,
  ): Observable<GridValuesPage>;
}

export interface GridDataSourceOptions<TRow> {
  url: string;
  rowSchema: z.ZodType<TRow, z.ZodTypeDef, unknown>;
  columns: GridColumns<TRow>;
}

/** Creates a data source; call it in an injection context (a field initializer). */
export function createGridDataSource<TRow>(
  options: GridDataSourceOptions<TRow>,
): GridDataSource<TRow> {
  const http = inject(HttpClient);
  const pageSchema = GridPageSchema(options.rowSchema);

  const post = <T>(
    url: string,
    body: unknown,
    schema: z.ZodType<T, z.ZodTypeDef, unknown>,
  ): Observable<T> =>
    // Values travel only in the POST body, never in the URL.
    http.post<unknown>(url, body).pipe(
      catchError((err: unknown) =>
        throwError(() =>
          err instanceof HttpErrorResponse
            ? toGridRequestError(err.status, err.error)
            : new GridRequestError('network'),
        ),
      ),
      map((body) => {
        const parsed = schema.safeParse(body);
        if (!parsed.success) throw new GridRequestError('response');
        return parsed.data;
      }),
    );

  // defer(): mapping runs on subscribe, so a mapping error is an Observable error like any other.
  const build = <T>(fn: () => T): Observable<T> =>
    defer(() => {
      try {
        return [fn()];
      } catch (err) {
        if (err instanceof GridRequestMappingError) {
          return throwError(() => new GridRequestError('mapping', { fields: [err.field] }));
        }
        throw err;
      }
    });

  return {
    url: options.url,
    columns: options.columns,
    fetchPage: (state) =>
      build(() => toGridRequest(options.columns, state)).pipe(
        tap((request) => logRequest(options.columns.grid, request)),
        switchMap((request) => post(options.url, request, pageSchema)),
      ),
    fetchValues: (field, filterModel, search, timeZone) =>
      build(() => toGridValuesRequest(options.columns, field, filterModel, search, timeZone)).pipe(
        tap((request) => logRequest(options.columns.grid, request)),
        switchMap((request) => post(`${options.url}/values`, request, GridValuesPageSchema)),
      ),
  };
}

function logRequest(grid: string, request: GridRequest | GridValuesRequest): void {
  console.debug(describeGridRequest(grid, request));
}
