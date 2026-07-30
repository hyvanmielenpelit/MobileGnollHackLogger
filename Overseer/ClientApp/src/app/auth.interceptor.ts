import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  // Angular's built-in HttpClientXsrfModule handles extracting XSRF-TOKEN from cookies 
  // and sending X-XSRF-TOKEN header automatically, but since this is a standalone component setup,
  // we can configure it using withXsrfConfiguration in app.config.ts.
  
  // We can just rely on provideHttpClient(withXsrfConfiguration()) for CSRF.
  // This interceptor can be used for logging or handling 401s if needed.
  return next(req);
};
