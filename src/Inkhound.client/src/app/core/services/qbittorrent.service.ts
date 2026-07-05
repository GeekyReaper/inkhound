import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface QBittorrentCategory {
  name: string;
  savePath: string;
}

export type DownloadStatus = 'Downloading' | 'Paused' | 'Finished' | 'Syncing' | 'Done' | 'Error' | 'Unknown';

export interface DownloadItem {
  id: string;
  issueId: string;
  torrentHash: string;
  status: DownloadStatus;
  addedAt: string;
  updatedAt: string | null;
  issueNumber: number | null;
  issueTitle: string | null;
  volumeTitle: string | null;
  torrentName: string | null;
  progress: number | null;
  dlspeed: number | null;
  eta: number | null;
  size: number | null;
}

export interface TorrentFile {
  index: number;
  name: string;
  size: number;
}

export interface GrabResponse {
  torrentHash: string | null;
  files: TorrentFile[] | null;
}

@Injectable({ providedIn: 'root' })
export class QBittorrentService {
  private http = inject(HttpClient);

  getCategories() {
    return this.http.get<QBittorrentCategory[]>('/api/qbittorrent/categories');
  }

  grab(downloadUrl: string, issueId: string, selective = false) {
    return this.http.post<GrabResponse>('/api/qbittorrent/grab', { downloadUrl, issueId, selective });
  }

  applySelection(torrentHash: string, issueId: string, selectedFileIndices: number[]) {
    return this.http.post<void>('/api/qbittorrent/apply-selection', { torrentHash, issueId, selectedFileIndices });
  }

  getDownloads(status?: DownloadStatus) {
    const params: Record<string, string> = {};
    if (status) params['status'] = status;
    return this.http.get<DownloadItem[]>('/api/qbittorrent/downloads', { params });
  }

  processDownloads() {
    return this.http.post<{ message: string }>('/api/qbittorrent/downloads/process', {});
  }

  processDownload(id: string) {
    return this.http.post<{ message: string }>(`/api/qbittorrent/downloads/${id}/process`, {});
  }
}
