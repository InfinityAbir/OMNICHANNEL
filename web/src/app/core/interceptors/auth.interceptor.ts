import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

const AUTH_FREE_PATHS = ['/api/v1/auth/'];

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const isAuthEndpoint = AUTH_FREE_PATHS.some((path) => req.url.includes(path));
  const token = auth.accessToken;
  const authedReq = !isAuthEndpoint && token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(authedReq).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && !isAuthEndpoint) {
        return from(auth.refreshAccessToken()).pipe(
          switchMap((refreshed) => {
            if (!refreshed) {
              void router.navigate(['/login']);
              return throwError(() => error);
            }

            const retried = req.clone({ setHeaders: { Authorization: `Bearer ${auth.accessToken}` } });
            return next(retried);
          }),
        );
      }

      return throwError(() => error);
    }),
  );
};
