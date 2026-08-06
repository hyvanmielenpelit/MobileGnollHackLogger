import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, RouteReuseStrategy } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { provideCharts } from 'ng2-charts';
import { BarController, BarElement, CategoryScale, LinearScale, Legend, Tooltip } from 'chart.js';

import { routes } from './app.routes';
import { authInterceptor } from './auth.interceptor';
import { CustomRouteReuseStrategy } from './custom-route-reuse-strategy';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }), 
    provideRouter(routes),
    { provide: RouteReuseStrategy, useClass: CustomRouteReuseStrategy },
    provideHttpClient(
      withFetch(),
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
      withInterceptors([authInterceptor])
    ),
    provideCharts({ registerables: [BarController, BarElement, CategoryScale, LinearScale, Legend, Tooltip] })
  ]
};
