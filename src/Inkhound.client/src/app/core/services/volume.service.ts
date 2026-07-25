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
  | 'Unknown' | 'RatingPending' | 'EarlyChildhood' | 'Everyone' | 'G'
  | 'Everyone10Plus' | 'PG' | 'KidsToAdults' | 'Teen' | 'MA15Plus'
  | 'Mature17Plus' | 'M' | 'R18Plus' | 'AdultsOnly18Plus' | 'X18Plus';

export interface AgeRatingOption { value: AgeRating; label: string; }

export const AGE_RATINGS: AgeRatingOption[] = [
  { value: 'Unknown',          label: 'Unknown' },
  { value: 'RatingPending',    label: 'Rating Pending' },
  { value: 'EarlyChildhood',   label: 'Early Childhood' },
  { value: 'Everyone',         label: 'Everyone' },
  { value: 'G',                label: 'G' },
  { value: 'Everyone10Plus',   label: 'Everyone 10+' },
  { value: 'PG',               label: 'PG' },
  { value: 'KidsToAdults',     label: 'Kids to Adults' },
  { value: 'Teen',             label: 'Teen' },
  { value: 'MA15Plus',         label: 'MA15+' },
  { value: 'Mature17Plus',     label: 'Mature 17+' },
  { value: 'M',                label: 'M' },
  { value: 'R18Plus',          label: 'R18+' },
  { value: 'AdultsOnly18Plus', label: 'Adults Only 18+' },
  { value: 'X18Plus',          label: 'X18+' },
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

export type SourceKey = 'comicvine' | 'bedetheque';

export interface VolumeSearchResult {
  sourceId:       string;
  source:         SourceKey;
  title:          string;
  year:           number | null;
  countOfIssues:  number;
  description:    string | null;
  publisher:      string | null;
  imageUrl:       string | null;
  siteUrl:        string | null;
  score:          number;
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

export interface SourceSearchStats {
  source:       SourceKey;
  resultCount:  number;
  elapsedMs:    number;
  success:      boolean;
  errorMessage: string | null;
}

export interface SearchVolumesJobResult {
  page:  PageResult<VolumeSearchResult>;
  stats: SourceSearchStats[];
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

  // Lance la recherche multi-source comme un Job (peut prendre plusieurs secondes — scraping
  // Bedetheque compris) et retourne son jobId. Le résultat se récupère ensuite via
  // getSearchJobResult() une fois le job terminé (suivre sa progression via HubService).
  startSearchJob(name: string, page = 1, pageSize = 16) {
    return this.http.post<{ jobId: string }>(`/api/volumes/search`, { name, page, pageSize });
  }

  getSearchJobResult(jobId: string) {
    return this.http.get<SearchVolumesJobResult>(`/api/volumes/search/${jobId}`);
  }

  importFromDirectory(volumeId: string, importDirectory: string) {
    return this.http.post<{ message: string }>(
      `/api/volumes/${volumeId}/import`,
      { importDirectory }
    );
  }

  // Le Volume est créé immédiatement (retourné tout de suite) ; le peuplement des issues se
  // poursuit en tâche de fond — jobId permet de suivre sa progression (cf. PageJobService).
  addFromSource(libraryId: string, source: SourceKey, sourceId: string) {
    return this.http.post<{ id: string; jobId: string }>(
      `/api/libraries/${libraryId}/volumes`,
      { source, sourceId }
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

  rematchFromSource(volumeId: string, source: SourceKey, sourceId: string) {
    return this.http.post<void>(`/api/volumes/${volumeId}/rematch`, { source, sourceId });
  }

  regenerateComicInfo(volumeId: string) {
    return this.http.post<{ message: string }>(`/api/volumes/${volumeId}/regenerate-comic-info`, {});
  }

  patchAgeRating(volumeId: string, ageRating: AgeRating) {
    return this.http.patch<void>(`/api/volumes/${volumeId}/age-rating`, { ageRating });
  }

  delete(id: string) {
    return this.http.delete<void>(`/api/volumes/${id}`);
  }
}
