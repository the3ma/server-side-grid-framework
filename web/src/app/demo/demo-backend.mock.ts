import { HttpErrorResponse, type HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { delay, mergeMap, of, throwError } from 'rxjs';
import type { GridRequest, GridValuesRequest } from '../grid/grid-request.types';
import { MockGridBackend } from './mock-grid-backend';

let backend: MockGridBackend | undefined;

/** Demo mode: answers /api/shipments/grid(/values) in memory with the same rules as the API. */
const mockGridInterceptor: HttpInterceptorFn = (req, next) => {
  const path = new URL(req.url, 'http://local').pathname;
  if (req.method !== 'POST' || !path.startsWith('/api/shipments/grid')) return next(req);
  backend ??= new MockGridBackend();
  const caller = {
    tenant: Number(req.headers.get('X-Demo-Tenant') ?? '1'),
    finance: req.headers.get('X-Demo-Finance') === 'true',
  };
  const result = path.endsWith('/values')
    ? backend.values(req.body as GridValuesRequest, caller)
    : backend.page(req.body as GridRequest, caller);
  return of(result).pipe(
    delay(250),
    mergeMap((r) =>
      r.status === 200
        ? of(new HttpResponse({ status: 200, body: r.body, url: req.url }))
        : throwError(
            () => new HttpErrorResponse({ status: r.status, error: r.body, url: req.url }),
          ),
    ),
  );
};

export const DEMO_BACKEND_INTERCEPTORS: HttpInterceptorFn[] = [mockGridInterceptor];
