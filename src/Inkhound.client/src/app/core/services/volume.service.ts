import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface VolumeImage {
  iconUrl:        string | null;
  mediumUrl:      string | null;
  screenUrl:      string | null;
  screenLargeUrl: string | null;
  smallUrl:       string | null;
  superUrl:       string | null;
  thumbUrl:       string | null;
  tinyUrl:        string | null;
  originalUrl:    string | null;
  imageTags:      string | null;
}

export interface VolumeAuthor {
  name: string;
  role: string;
}

export type VolumeStatus = 'MONITORED' | 'COMPLETED' | 'PAUSED';

export type AgeRating =
  | 'Unknown' | 'AllAges' | 'G' | 'PG' | 'Everyone10Plus'
  | 'Teen' | 'Mature17Plus' | 'Adults18Plus' | 'X18Plus';

export interface AgeRatingOption { value: AgeRating; label: string; }

export const AGE_RATINGS: AgeRatingOption[] = [
  { value: 'Unknown',       label: 'Unknown' },
  { value: 'AllAges',       label: 'All Ages' },
  { value: 'G',             label: 'G' },
  { value: 'PG',            label: 'PG' },
  { value: 'Everyone10Plus',label: 'Everyone 10+' },
  { value: 'Teen',          label: 'Teen' },
  { value: 'Mature17Plus',  label: 'Mature 17+' },
  { value: 'Adults18Plus',  label: 'Adults 18+' },
  { value: 'X18Plus',       label: 'X18+' },
];

export interface Volume {
  id:                       string;
  libraryId:                string;
  sourceId:                 string;
  sourceType:               string;
  title:                    string;
  year:                     number | null;
  description:              string | null;
  publisher:                string | null;
  status:                   VolumeStatus;
  ageRating:                AgeRating;
  genres:                   string[];
  authors:                  VolumeAuthor[];
  image:                    VolumeImage | null;
  countOfIssues:            number;
  countOfDownloadedIssues:  number;
  createdAt:                string;
  updatedAt:                string;
}

export interface VolumeSearchResult {
  sourceId:       string;
  sourceType:     string;
  title:          string;
  year:           number | null;
  countOfIssues:  number;
  description:    string | null;
  publisher:      string | null;
  image:          VolumeImage | null;
  firstIssueName: string | null;
  lastIssueName:  string | null;
  siteDetailUrl:  string | null;
}

export interface PageResult<T> {
  items:      T[];
  pageNumber: number;
  pageSize:   number;
  totalItems: number;
  totalPages: number;
  hasNext:    boolean;
  hasPrev:    boolean;
}

export interface ManualIssueRequest {
  issueNumber:  number;
  title:        string | null;
  year:         number | null;
  description:  string | null;
  imageUrl:     string | null;
}

export interface AddVolumeManuallyRequest {
  title:       string;
  year:        number | null;
  publisher:   string | null;
  description: string | null;
  imageUrl:    string | null;
  authors:     VolumeAuthor[];
  genres:      string[];
  issues:      ManualIssueRequest[];
}

export interface UpdateIssueRequest {
  id:          string | null;
  issueNumber: number;
  title:       string | null;
  year:        number | null;
  description: string | null;
  imageUrl:    string | null;
}

export interface UpdateVolumeManuallyRequest {
  title:       string;
  year:        number | null;
  publisher:   string | null;
  description: string | null;
  imageUrl:    string | null;
  authors:     VolumeAuthor[];
  genres:      string[];
  issues:      UpdateIssueRequest[];
}

@Injectable({ providedIn: 'root' })
export class VolumeService {
  private http = inject(HttpClient);

  getById(id: string) {
    return this.http.get<Volume>(`/api/volumes/${id}`);
  }

  getByLibrary(libraryId: string) {
    return this.http.get<Volume[]>(`/api/libraries/${libraryId}/volumes`);
  }

  search(name: string, page = 1, pageSize = 16) {
    return this.http.get<PageResult<VolumeSearchResult>>(`/api/volumes/search`, {
      params: { name, page, pageSize }
    });
  }

  importFromDirectory(volumeId: string, importDirectory: string) {
    return this.http.post<{ message: string }>(
      `/api/volumes/${volumeId}/import`,
      { importDirectory }
    );
  }

  addFromComicVine(libraryId: string, comicVineVolumeId: string) {
    return this.http.post<{ id: string }>(
      `/api/libraries/${libraryId}/volumes`,
      { comicVineVolumeId: Number(comicVineVolumeId) }
    );
  }

  addManually(libraryId: string, request: AddVolumeManuallyRequest) {
    return this.http.post<{ id: string }>(
      `/api/libraries/${libraryId}/volumes/manual`,
      request
    );
  }

  update(volumeId: string, request: UpdateVolumeManuallyRequest) {
    return this.http.put<void>(`/api/volumes/${volumeId}`, request);
  }

  rematchFromComicVine(volumeId: string, comicVineVolumeId: string) {
    return this.http.post<void>(`/api/volumes/${volumeId}/rematch`, { comicVineVolumeId: Number(comicVineVolumeId) });
  }

  patchAgeRating(volumeId: string, ageRating: AgeRating) {
    return this.http.patch<void>(`/api/volumes/${volumeId}/age-rating`, { ageRating });
  }

  delete(id: string) {
    return this.http.delete<void>(`/api/volumes/${id}`);
  }
}
