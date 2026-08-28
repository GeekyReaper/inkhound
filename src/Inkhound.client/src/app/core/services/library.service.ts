import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { tap } from 'rxjs';
import { RefreshVolumeOptions } from './volume.service';

export interface Library {
  id: string;
  name: string;
  path: string;
  kavitaLibraryId: number;
  kavitaPath: string;
  createdAt: string;
}

export interface CreateLibraryRequest {
  name: string;
  path: string;
  kavitaLibraryId: number;
  kavitaPath: string;
}

export type UpdateLibraryRequest = CreateLibraryRequest;

// Clé de page pour PageJobService — même format que router.url une fois sur la page Library,
// utilisée à la fois par LibraryComponent (pour s'y abonner) et par les pages qui y associent un
// job lancé ailleurs (ex: VolumeAddComponent après un "Add to Library").
export function libraryPageKey(libraryId: string): string {
  return `/library/${libraryId}`;
}

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

  recalculateStatistics(id: string) {
    return this.http.post<{ message: string }>(`/api/libraries/${id}/recalculate-statistics`, null);
  }

  refresh(libraryId: string, options: RefreshVolumeOptions) {
    return this.http.post<{ jobIds: string[] }>(`/api/libraries/${libraryId}/refresh`, options);
  }
}
