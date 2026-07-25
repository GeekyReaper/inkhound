import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface ProwlarrCategory {
  id: number;
  name: string;
}

export interface ProwlarrIndexer {
  id: number;
  name: string;
  protocol: string;
  enable: boolean;
  categories: ProwlarrCategory[];
}

export interface SelectedIndexer {
  indexerId: number;
  name: string;
  protocol: string;
  selectedCategoryIds: number[];
}

export interface IndexerSelection {
  id: number;
  name: string;
  protocol: string;
  enable: boolean;
  selectedCategoryIds: number[];
}

export interface ProwlarrSearchResult {
  title: string;
  infoUrl: string | null;
  size: number;
  seeders: number;
  leechers: number;
  guid: string;
  indexerId: number;
  indexer: string | null;
  protocol: string | null;
  downloadUrl: string | null;
  publishDate: string | null;
  categories: ProwlarrCategory[];
  torrentType: 'SINGLE' | 'PACK' | 'UNKNOWN';
  torrentLabel: string;
}

export interface ScoreDetails {
  titleMatch: number;
  issueNumberMatch: number;
  yearMatch: number;
  sizePlausibility: number;
  seederScore: number;
  formatScore: number;
}

export interface ScoredSearchResult {
  result: ProwlarrSearchResult;
  score: number;
  details: ScoreDetails;
}

export interface ProwlarrHistoryItem {
  id: number;
  eventType: string;
  sourceTitle: string;
  indexerId: number;
}

@Injectable({ providedIn: 'root' })
export class ProwlarrService {
  private http = inject(HttpClient);

  getIndexers() {
    return this.http.get<ProwlarrIndexer[]>('/api/prowlarr/indexers');
  }

  getSelectedIndexers() {
    return this.http.get<SelectedIndexer[]>('/api/prowlarr/selected-indexers');
  }

  setSelectedIndexers(indexers: IndexerSelection[]) {
    return this.http.put<void>('/api/prowlarr/selected-indexers', { indexers });
  }

  // Lance la recherche Prowlarr comme un Job (peut prendre plusieurs secondes — un appel par
  // requête essayée) et retourne son jobId. Le résultat se récupère ensuite via
  // getSearchJobResult() une fois le job terminé (suivre sa progression via HubService).
  startSearchJob(issueId: string, indexerIds?: number[]) {
    let params: Record<string, string | string[]> = {};
    if (indexerIds && indexerIds.length > 0) {
      params['indexerIds'] = indexerIds.map(id => id.toString());
    }
    return this.http.post<{ jobId: string }>(`/api/prowlarr/search/${issueId}`, null, { params });
  }

  getSearchJobResult(jobId: string) {
    return this.http.get<ScoredSearchResult[]>(`/api/prowlarr/search/${jobId}/result`);
  }

  grab(guid: string, indexerId: number, issueId: string, downloadClientId?: number) {
    return this.http.post<{ message: string }>('/api/prowlarr/grab', { guid, indexerId, issueId, downloadClientId });
  }

  getHistory(limit = 20) {
    return this.http.get<ProwlarrHistoryItem[]>('/api/prowlarr/history', { params: { limit } });
  }
}
