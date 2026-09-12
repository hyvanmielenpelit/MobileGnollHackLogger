import { ApplicationConfig, provideZoneChangeDetection, ErrorHandler } from '@angular/core';
import { provideRouter, RouteReuseStrategy } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { provideCharts } from 'ng2-charts';
import * as Sentry from '@sentry/angular';

import { routes } from './app.routes';
import { authInterceptor } from './auth.interceptor';
import { CustomRouteReuseStrategy } from './custom-route-reuse-strategy';
import { APP_CHART_REGISTRABLES } from './chart-registrables';

export const appConfig: ApplicationConfig = {
  providers: [
    { provide: ErrorHandler, useValue: Sentry.createErrorHandler() },
    provideZoneChangeDetection({ eventCoalescing: true }), 
    provideRouter(routes),
    { provide: RouteReuseStrategy, useClass: CustomRouteReuseStrategy },
    provideHttpClient(
      withFetch(),
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
      withInterceptors([authInterceptor])
    ),
    provideCharts({ registerables: APP_CHART_REGISTRABLES })
  ]
};
