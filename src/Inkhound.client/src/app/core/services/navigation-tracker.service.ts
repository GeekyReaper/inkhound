import { DestroyRef, inject, Injectable } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router } from '@angular/router';
import { filter } from 'rxjs';

export type NavigationTrigger = 'imperative' | 'popstate' | 'hashchange';

// Mémorise le déclencheur de la dernière navigation router (imperative / popstate / hashchange).
// `NavigationStart` est émis très tôt — avant la (re)création du composant cible — donc un composant
// ne peut pas le capter lui‑même de façon fiable via `getCurrentNavigation()`. Ce service, instancié
// tôt (injecté par `AppComponent`), garde la dernière valeur disponible pour distinguer un retour
// arrière/avant navigateur d'une navigation applicative (clic lien, sidebar…).
@Injectable({ providedIn: 'root' })
export class NavigationTrackerService {
  private _lastTrigger: NavigationTrigger = 'imperative';

  get lastTrigger(): NavigationTrigger {
    return this._lastTrigger;
  }

  // `withHashLocation()` est actif : un back/forward navigateur remonte en `'popstate'` ou
  // `'hashchange'` selon le navigateur.
  get isBackForward(): boolean {
    return this._lastTrigger === 'popstate' || this._lastTrigger === 'hashchange';
  }

  constructor() {
    inject(Router).events
      .pipe(
        filter((e): e is NavigationStart => e instanceof NavigationStart),
        takeUntilDestroyed(inject(DestroyRef))
      )
      .subscribe(e => { this._lastTrigger = e.navigationTrigger ?? 'imperative'; });
  }
}
