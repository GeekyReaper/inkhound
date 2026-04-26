import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { tap } from 'rxjs';

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
  private _currentUser = signal<CurrentUser | null>(this.parseUserFromStorage());

  currentUser = this._currentUser.asReadonly();
  isAuthenticated = computed(() => !!this.token() && !!this._currentUser());

  login(login: string, password: string) {
    return this.http.post<LoginResponse>('/api/auth/login', { login, password }).pipe(
      tap(res => {
        const user = this.decodeJwt(res.token);
        localStorage.setItem('inkhound_token', res.token);
        this.token.set(res.token);
        this._currentUser.set(user);
      })
    );
  }

  logout() {
    localStorage.removeItem('inkhound_token');
    this.token.set(null);
    this._currentUser.set(null);
  }

  getToken() { return this.token(); }

  private parseUserFromStorage(): CurrentUser | null {
    const token = localStorage.getItem('inkhound_token');
    if (!token) return null;
    try { return this.decodeJwt(token); } catch { return null; }
  }

  private decodeJwt(token: string): CurrentUser {
    const base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const payload = JSON.parse(atob(base64));
    const role = payload.role
      ?? payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
      ?? '';
    return { id: payload.sub, login: payload.name, role };
  }
}
