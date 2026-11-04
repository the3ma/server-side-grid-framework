import { Injectable, signal } from '@angular/core';
import type { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

/**
 * Demo-only session switch. The example API signs every request in as tenant 1; these headers let
 * the demo switch tenant and add the `finance` role. Identity never travels in the grid body.
 */
@Injectable({ providedIn: 'root' })
export class DemoSession {
  readonly tenant = signal<1 | 2 | 3>(1);
  readonly finance = signal(false);
}

export const demoSessionInterceptor: HttpInterceptorFn = (req, next) => {
  const session = inject(DemoSession);
  return next(
    req.clone({
      setHeaders: {
        'X-Demo-Tenant': String(session.tenant()),
        'X-Demo-Finance': String(session.finance()),
      },
    }),
  );
};
