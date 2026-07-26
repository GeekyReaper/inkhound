import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Observable, of } from 'rxjs';
import { catchError, shareReplay, switchMap, tap } from 'rxjs/operators';

interface LoginResponse {
  token: string;
  expiresAt: string;
  role: string;
}

export interface CurrentUser {
  id: string;
  login: string;
  role: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private token = signal<string | null>(localStorage.getItem('inkhound_token'));
  private _currentUser = signal<CurrentUser | null>(null);

  // Cache la résolution de session le temps du chargement d'app (voir resolveSession()) — réinitialisé
  // au logout pour forcer une nouvelle résolution à la prochaine navigation.
  private session$: Observable<CurrentUser | null> | null = null;

  currentUser = this._currentUser.asReadonly();

  // Résolu via /api/auth/me plutôt que par la seule présence d'un token local : en mode bootstrap
  // ouvert (aucun utilisateur en base côté backend), currentUser est peuplé sans qu'aucun token
  // n'ait jamais existé.
  isAuthenticated = computed(() => !!this._currentUser());

  login(login: string, password: string) {
    // Invalide le cache de resolveSession() : sans ça, authGuard rejouerait après la navigation vers
    // /dashboard l'Observable mis en cache AVANT la connexion (résolu à null via shareReplay), et
    // renverrait aussitôt l'utilisateur vers /login malgré un login réussi.
    this.session$ = null;
    return this.http.post<LoginResponse>('/api/auth/login', { login, password }).pipe(
      switchMap(res => {
        localStorage.setItem('inkhound_token', res.token);
        this.token.set(res.token);
        return this.fetchMe();
      })
    );
  }

  logout() {
    localStorage.removeItem('inkhound_token');
    this.token.set(null);
    this._currentUser.set(null);
    this.session$ = null;
  }

  getToken() { return this.token(); }

  // Appelée par authGuard — ne fait l'appel réseau qu'une fois par chargement d'app (mis en cache via
  // shareReplay), résout la session courante que ce soit via un vrai JWT ou via le bypass "mode
  // bootstrap ouvert" côté backend (voir Inkhound.Web/CLAUDE.md, section Auth JWT).
  resolveSession(): Observable<CurrentUser | null> {
    if (!this.session$) {
      this.session$ = this.fetchMe().pipe(shareReplay(1));
    }
    return this.session$;
  }

  private fetchMe(): Observable<CurrentUser | null> {
    return this.http.get<CurrentUser>('/api/auth/me').pipe(
      tap(user => this._currentUser.set(user)),
      catchError(() => {
        this._currentUser.set(null);
        return of(null);
      })
    );
  }
}
