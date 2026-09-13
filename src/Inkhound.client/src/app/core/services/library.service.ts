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

// GET /api/libraries/{id}/stats — encart de la page Library. volumesBySource indexé par
// SourceType en minuscules ("comicvine", "bedetheque", "manual").
export interface LibraryStats {
  volumesCount:         number;
  volumesMonitored:     number;
  volumesCompleted:     number;
  volumesPaused:        number;
  volumesBySource:      Record<string, number>;
  issuesCount:          number;
  issuesDownloaded:     number;
  issuesDownloading:    number;
  issuesMissing:        number;
  totalDownloadedBytes: number;
  lastVolumeAddedAt:    string | null;
  lastRefreshedAt:      string | null;
  lastAutoSearchAt:     string | null;
}

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

  getStats(id: string) {
    return this.http.get<LibraryStats>(`/api/libraries/${id}/stats`);
  }

  create(request: CreateLibraryRequest) {
    return this.http.post<Library>('/api/libraries', request);
  }

  update(id: string, request: UpdateLibraryRequest) {
    return this.http.put<Library>(`/api/libraries/${id}`, request);
  }

  // 204, ou 200 { fileWarning } si un répertoire de volume n'a pas pu être supprimé.
  delete(id: string, deleteFiles = false) {
    return this.http.delete<{ fileWarning?: string } | null>(`/api/libraries/${id}`, { params: { deleteFiles } });
  }

  sync(id: string) {
    return this.http.post<{ message: string }>(`/api/libraries/${id}/sync`, null);
  }

  recalculateStatistics(id: string) {
    return this.http.post<{ message: string }>(`/api/libraries/${id}/recalculate-statistics`, null);
  }

  // Bascule en masse : 'PAUSED' met en pause tous les volumes MONITORED (incomplets) de la library,
  // 'MONITORED' reprend tous les PAUSED. Les COMPLETED ne sont jamais touchés.
  patchVolumesStatus(libraryId: string, status: 'MONITORED' | 'PAUSED') {
    return this.http.patch<{ updated: number }>(`/api/libraries/${libraryId}/volumes/status`, { status });
  }

  refresh(libraryId: string, options: RefreshVolumeOptions) {
    return this.http.post<{ jobIds: string[] }>(`/api/libraries/${libraryId}/refresh`, options);
  }
}
