import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs/operators';
import { AuthService } from '../services/auth.service';

// Réserve une route aux administrateurs (pages des sections Settings / Access du menu) : un Guest est
// renvoyé vers le dashboard. À poser en plus de authGuard (porté par le layout parent).
export const adminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.resolveSession().pipe(
    map(user => user?.role === 'admin' || router.createUrlTree(['/dashboard']))
  );
};
