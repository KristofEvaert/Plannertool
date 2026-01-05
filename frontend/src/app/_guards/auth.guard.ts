import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '@services/auth.service';

export const authGuard: CanActivateFn = (route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const requiredRoles = (route.data && (route.data['roles'] as string[])) || null;

  if (!auth.isAuthenticated()) {
    router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
    return false;
  }

  if (requiredRoles && !auth.hasAnyRole(requiredRoles)) {
    router.navigate(['/start']);
    return false;
  }

  return true;
};
