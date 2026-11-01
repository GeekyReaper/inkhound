import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { tap } from 'rxjs';

import { UserRole } from './auth.service';
import { AgeRating } from './volume.service';

export interface User {
  id: string;
  login: string;
  role: UserRole;
  /** Plafond de classification d'âge (Guest) — null = aucune limite. */
  maxAgeRating: AgeRating | null;
  createdAt: string;
}

export interface CreateUserRequest {
  login: string;
  password: string;
  role: UserRole;
  maxAgeRating: AgeRating | null;
}

export interface UpdateUserRequest {
  login: string | null;
  password: string | null;
  role: UserRole | null;
  /** '' = retire la limite. */
  maxAgeRating: AgeRating | '' | null;
}

@Injectable({ providedIn: 'root' })
export class UserService {
  private http = inject(HttpClient);

  private _users = signal<User[]>([]);
  readonly users = this._users.asReadonly();

  loadUsers() {
    return this.getAll().pipe(tap(users => this._users.set(users)));
  }

  getAll() {
    return this.http.get<User[]>('/api/users');
  }

  create(request: CreateUserRequest) {
    return this.http.post<User>('/api/users', request);
  }

  update(id: string, request: UpdateUserRequest) {
    return this.http.put<User>(`/api/users/${id}`, request);
  }

  delete(id: string) {
    return this.http.delete(`/api/users/${id}`);
  }
}
