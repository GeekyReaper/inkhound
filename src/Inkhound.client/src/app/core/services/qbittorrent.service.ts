import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface QBittorrentCategory {
  name: string;
  savePath: string;
}

export type DownloadStatus = 'Downloading' | 'Paused' | 'Finished' | 'Error' | 'Unknown';

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

@Injectable({ providedIn: 'root' })
export class QBittorrentService {
  private http = inject(HttpClient);

  getCategories() {
    return this.http.get<QBittorrentCategory[]>('/api/qbittorrent/categories');
  }

  grab(downloadUrl: string, issueId: string) {
    return this.http.post<{ torrentHash: string | null }>('/api/qbittorrent/grab', { downloadUrl, issueId });
  }

  getDownloads(status?: DownloadStatus) {
    const params: Record<string, string> = {};
    if (status) params['status'] = status;
    return this.http.get<DownloadItem[]>('/api/qbittorrent/downloads', { params });
  }
}
