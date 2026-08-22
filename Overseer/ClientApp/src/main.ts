import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';

import * as Sentry from '@sentry/angular';
import packageJson from '../package.json';
import { sentryBeforeSend } from './app/utils/sentry-filter.util';

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
  integrations: (integrations) => integrations.filter(i => i.name !== 'BrowserSession'),
  sendClientReports: false,
  beforeSend: sentryBeforeSend
});

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));
