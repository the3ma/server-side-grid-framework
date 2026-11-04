import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  type ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { DEMO_BACKEND_INTERCEPTORS } from './demo/demo-backend';
import { demoSessionInterceptor } from './demo/demo-session';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideHttpClient(withInterceptors([demoSessionInterceptor, ...DEMO_BACKEND_INTERCEPTORS])),
  ],
};
