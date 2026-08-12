import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, firstValueFrom, throwError } from 'rxjs';
import { ApiService } from './api.service';

export const authGuard: CanActivateFn = async () => {
  const api = inject(ApiService);
  const router = inject(Router);
  try {
    await firstValueFrom(api.me());
    return true;
  } catch {
    return router.createUrlTree(['/auth']);
  }
};

export const unauthorizedInterceptor: HttpInterceptorFn = (request, next) => {
  const router = inject(Router);
  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && !request.url.includes('/api/auth/')) {
        void router.navigate(['/auth']);
      }
      return throwError(() => error);
    })
  );
};

