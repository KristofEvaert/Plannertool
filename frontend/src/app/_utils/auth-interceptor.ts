import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpRequest } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import type { ApiError, ProblemDetails } from '@models/problem-details.model';

export function authInterceptor(
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
): Observable<HttpEvent<unknown>> {
  const token = localStorage.getItem('tp_token');
  const authReq = token
    ? req.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`,
        },
      })
    : req;

  return next(authReq).pipe(
    catchError((error: HttpErrorResponse) => {
      // Check if error response has ProblemDetails format
      if (
        error.error &&
        typeof error.error === 'object' &&
        (error.error.title !== undefined || error.error.status !== undefined)
      ) {
        const problemDetails = error.error as ProblemDetails;
        const apiError: ApiError = {
          name: 'ApiError',
          message: problemDetails.detail || problemDetails.title || 'An error occurred',
          status: problemDetails.status || error.status || 500,
          title: problemDetails.title || 'Error',
          detail: problemDetails.detail,
          originalError: error,
        };
        return throwError(() => apiError);
      }

      // For other errors, rethrow as-is
      return throwError(() => error);
    })
  );
}
