import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface QBittorrentCategory {
  name: string;
  savePath: string;
}

export type DownloadStatus = 'Downloading' | 'Paused' | 'Finished' | 'Syncing' | 'Done' | 'Error' | 'Unknown' | 'NotFound';

export interface DownloadItem {
  id: string;
  issueId: string;
  torrentHash: string;
  torrentTitle: string;
  trackerName: string | null;
  status: DownloadStatus;
  addedAt: string;
  updatedAt: string | null;
  issueNumber: number | null;
  issueTitle: string | null;
  volumeTitle: string | null;
  progress: number | null;
  dlspeed: number | null;
  eta: number | null;
  size: number | null;
}

export interface TorrentFile {
  index: number;
  name: string;
  size: number;
  detectedIssueNumber: number | null;
}

export interface GrabResponse {
  torrentHash: string | null;
  files: TorrentFile[] | null;
}

export interface DownloadsPageResult {
  items: DownloadItem[];
  pageNumber: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  hasNext: boolean;
  hasPrev: boolean;
}

@Injectable({ providedIn: 'root' })
export class QBittorrentService {
  private http = inject(HttpClient);

  getCategories() {
    return this.http.get<QBittorrentCategory[]>('/api/qbittorrent/categories');
  }

  // issueId (flux page Issue) ou volumeId (flux page Volume) — passer l'autre à null.
  // issueId est requis quand selective=false (grab direct, cible toujours une issue précise).
  // title/trackerName sont persistés sur l'IssueDownload créé (identité propre de l'item, cf.
  // page Downloads qui n'affiche plus une donnée live QBittorrent pour son titre).
  grab(
    downloadUrl: string,
    title: string,
    trackerName: string | null,
    issueId: string | null,
    volumeId: string | null,
    selective = false
  ) {
    return this.http.post<GrabResponse>('/api/qbittorrent/grab', {
      downloadUrl, title, trackerName, issueId, volumeId, selective
    });
  }

  // fileIssueOverrides : index de fichier → issueId, pour les fichiers dont le numéro de tome n'a
  // pas pu être extrait automatiquement (assignation manuelle) — ou pour confirmer une association.
  applySelection(
    torrentHash: string,
    downloadUrl: string,
    title: string,
    trackerName: string | null,
    issueId: string | null,
    volumeId: string | null,
    selectedFileIndices: number[],
    fileIssueOverrides?: Record<number, string>
  ) {
    return this.http.post<void>('/api/qbittorrent/apply-selection', {
      torrentHash, downloadUrl, title, trackerName, issueId, volumeId, selectedFileIndices, fileIssueOverrides
    });
  }

  // statuses vide/omis = pas de filtre côté backend (tous les statuts).
  getDownloads(statuses?: DownloadStatus[], page = 1, pageSize = 20) {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    for (const s of statuses ?? []) params = params.append('status', s);
    return this.http.get<DownloadsPageResult>('/api/qbittorrent/downloads', { params });
  }

  processDownloads() {
    return this.http.post<{ message: string }>('/api/qbittorrent/downloads/process', {});
  }

  processDownload(id: string) {
    return this.http.post<{ message: string }>(`/api/qbittorrent/downloads/${id}/process`, {});
  }

  // Revérifie toute la table IssueDownloads contre l'état réel de QBittorrent (job).
  refreshDownloads() {
    return this.http.post<{ message: string }>('/api/qbittorrent/downloads/refresh', {});
  }

  updateDownloadHash(id: string, hash: string) {
    return this.http.put<DownloadItem>(`/api/qbittorrent/downloads/${id}/hash`, { hash });
  }

  deleteDownload(id: string) {
    return this.http.delete<void>(`/api/qbittorrent/downloads/${id}`);
  }
}
