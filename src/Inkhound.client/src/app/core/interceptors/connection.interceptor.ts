import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

export const connectionInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      if ((error.status === 0 || error.status===500 ) && router.url !== '/500') {
        router.navigate(['/500']);
      }
      return throwError(() => error);
    })
  );
};
