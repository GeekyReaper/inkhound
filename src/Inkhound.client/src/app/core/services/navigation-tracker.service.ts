import { DestroyRef, inject, Injectable } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router } from '@angular/router';
import { filter } from 'rxjs';

export type NavigationTrigger = 'imperative' | 'popstate' | 'hashchange';

// Suit la dernière navigation router : son déclencheur (imperative / popstate / hashchange) et
// l'URL quittée. `NavigationStart` est émis très tôt — avant la (re)création du composant cible —
// donc un composant ne peut pas capter ça lui‑même de façon fiable via `getCurrentNavigation()`.
// Ce service, instancié tôt (injecté par `AppComponent`), garde ces infos disponibles pour
// décider p. ex. s'il faut restaurer une position de scroll (retour depuis une sous‑page vs
// arrivée fraîche).
@Injectable({ providedIn: 'root' })
export class NavigationTrackerService {
  private readonly router = inject(Router);
  private _lastTrigger: NavigationTrigger = 'imperative';
  private _previousUrl = '';

  get lastTrigger(): NavigationTrigger {
    return this._lastTrigger;
  }

  // `withHashLocation()` est actif : un back/forward navigateur remonte en `'popstate'` ou
  // `'hashchange'` selon le navigateur.
  get isBackForward(): boolean {
    return this._lastTrigger === 'popstate' || this._lastTrigger === 'hashchange';
  }

  // URL (sans le `#`) sur laquelle on était juste avant la navigation courante.
  get previousUrl(): string {
    return this._previousUrl;
  }

  // Vrai si on vient de quitter une sous‑page de `baseUrl` (ex. `/library/{id}/volume/...` →
  // `/library/{id}`) OU si c'est un back/forward navigateur. Sert à ne restaurer le scroll d'une
  // page que lorsqu'on y « revient », pas lors d'une arrivée depuis la sidebar / un autre écran.
  isReturnInto(baseUrl: string): boolean {
    return this.isBackForward || this._previousUrl.startsWith(`${baseUrl}/`);
  }

  constructor() {
    this.router.events
      .pipe(
        filter((e): e is NavigationStart => e instanceof NavigationStart),
        takeUntilDestroyed(inject(DestroyRef))
      )
      .subscribe(e => {
        this._lastTrigger = e.navigationTrigger ?? 'imperative';
        // `router.url` n'est pas encore mis à jour au NavigationStart (urlUpdateStrategy 'deferred')
        // → c'est l'URL qu'on quitte.
        this._previousUrl = this.router.url;
      });
  }
}
