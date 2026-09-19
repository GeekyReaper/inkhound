import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

// État d'une lettre de l'index alphabétique du catalogue local Bedetheque (0, A-Z).
export interface BedethequeCatalogLetterStatus {
  letter: string;
  count: number;
  fetchedAtUtc: string | null;   // null = jamais chargée
}

export interface BedethequeCatalogStatus {
  loaded: boolean;               // au moins une série en mémoire → la recherche Bedetheque fonctionne
  totalSeries: number;
  oldestFetchUtc: string | null;
  newestFetchUtc: string | null;
  refreshRunning: boolean;
  letters: BedethequeCatalogLetterStatus[];
}

// letters (explicites) prioritaire sur letterCount (rotation, les plus anciennes d'abord) ;
// ni l'un ni l'autre = les 27 lettres.
export interface RefreshCatalogRequest {
  letterCount?: number;
  letters?: string[];
}

@Injectable({ providedIn: 'root' })
export class BedethequeCatalogService {
  private http = inject(HttpClient);

  getStatus() {
    return this.http.get<BedethequeCatalogStatus>('/api/bedetheque/catalog');
  }

  // 202 { jobId } — 409 si un rafraîchissement est déjà en cours.
  refresh(request: RefreshCatalogRequest) {
    return this.http.post<{ jobId: string }>('/api/bedetheque/catalog/refresh', request);
  }
}
