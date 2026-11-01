import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Observable, of } from 'rxjs';
import { catchError, shareReplay, switchMap, tap } from 'rxjs/operators';
import { AGE_RATINGS, AgeRating, AgeRatingOption } from './volume.service';

export type UserRole = 'admin' | 'guest';

interface LoginResponse {
  token: string;
  expiresAt: string;
  role: UserRole;
}

export interface CurrentUser {
  id: string;
  login: string;
  role: UserRole;
  /** Plafond de classification d'âge (null = aucune limite) — voir User.MaxAgeRating côté backend. */
  maxAgeRating?: AgeRating | null;
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

  // Admin : accès à tout. Guest : ni les sections Settings / Access / Links du menu, ni leurs pages
  // (adminGuard) ni leurs API (403 côté backend). Le mode bootstrap ouvert est servi comme admin.
  isAdmin = computed(() => this._currentUser()?.role === 'admin');

  // Plafond d'age rating du compte : les classifications strictement au-dessus ne sont ni visibles ni
  // demandables. La liste ci-dessous alimente tout sélecteur d'age rating proposé à un non-admin.
  maxAgeRating = computed<AgeRating | null>(() => this._currentUser()?.maxAgeRating ?? null);
  allowedAgeRatings = computed<AgeRatingOption[]>(() => {
    const max = this.maxAgeRating();
    if (!max) return AGE_RATINGS;
    const limit = AGE_RATINGS.findIndex(r => r.value === max);
    // Unknown et Rating Pending (2 premières entrées) sont masqués sous un plafond, comme côté serveur.
    return AGE_RATINGS.filter((r, i) => i <= limit && r.value !== 'Unknown' && r.value !== 'RatingPending');
  });

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
