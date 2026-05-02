import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

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

  getAll() {
    return this.http.get<Library[]>('/api/libraries');
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
}
