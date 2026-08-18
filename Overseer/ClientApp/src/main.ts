import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';

import * as Sentry from '@sentry/angular';
import { HttpErrorResponse } from '@angular/common/http';
import packageJson from '../package.json';

if (!("popover" in HTMLElement.prototype)) {
  import("@oddbird/popover-polyfill");
}

const sentryFetchWithCredentials = (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
  return fetch(input, { ...init, credentials: 'include' });
};

Sentry.init({
  dsn: 'https://placeholder@placeholder.ingest.sentry.io/0',
  tunnel: '/api/sentry/log',
  transport: (options) => Sentry.makeFetchTransport(options, sentryFetchWithCredentials),
  release: packageJson.version, // Automatically matches package.json and MSBuild SyncAngularVersion
  beforeSend(event: Sentry.ErrorEvent, hint: Sentry.EventHint) {
    const error = hint.originalException;
    // Drop ALL HttpErrorResponse instances:
    // - 5xx: Already captured on the backend (prevents double-logging)
    // - 4xx: User input validation or handled auth flows (not our bug)
    if (error instanceof HttpErrorResponse) {
      return null;
    }
    return event;
  }
});

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));
