import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const cloned = req.clone({ withCredentials: true });
  
  return next(cloned).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401) router.navigate(['/login']);
      return throwError(() => error);
    })
  );
};
