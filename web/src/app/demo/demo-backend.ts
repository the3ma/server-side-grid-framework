import type { HttpInterceptorFn } from '@angular/common/http';

/** Real API mode: no in-memory backend. `ng build --configuration mock` swaps this file. */
export const DEMO_BACKEND_INTERCEPTORS: HttpInterceptorFn[] = [];
