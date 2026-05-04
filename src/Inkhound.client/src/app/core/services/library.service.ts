import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { tap } from 'rxjs';

export interface Library {
  id: string;
  name: string;
  path: string;
  kavitaLibraryId: number;
  createdAt: string;
}

export interface CreateLibraryRequest {
  name: string;
  path: string;
  kavitaLibraryId: number;
}

export type UpdateLibraryRequest = CreateLibraryRequest;

@Injectable({ providedIn: 'root' })
export class LibraryService {
  private http = inject(HttpClient);

  private _libraries = signal<Library[]>([]);
  readonly libraries = this._libraries.asReadonly();

  loadLibraries() {
    return this.getAll().pipe(tap(libs => this._libraries.set(libs)));
  }

  getAll() {
    return this.http.get<Library[]>('/api/libraries');
  }

  getById(id: string) {
    return this.http.get<Library>(`/api/libraries/${id}`);
  }

  create(request: CreateLibraryRequest) {
    return this.http.post<Library>('/api/libraries', request);
  }

  update(id: string, request: UpdateLibraryRequest) {
    return this.http.put<Library>(`/api/libraries/${id}`, request);
  }

  delete(id: string) {
    return this.http.delete(`/api/libraries/${id}`);
  }

  sync(id: string) {
    return this.http.post<{ message: string }>(`/api/libraries/${id}/sync`, null);
  }
}
