import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { VolumeImage } from './volume.service';

export interface DashboardLibraryStats {
  id:                      string;
  name:                    string;
  volumesCount:            number;
  issuesCount:             number;
  downloadedIssuesCount:   number;
}

export interface DashboardRecentVolume {
  id:        string;
  libraryId: string;
  title:     string;
  image:     VolumeImage | null;
  dateAdded: string;
}

export interface DashboardStats {
  librariesCount:        number;
  volumesCount:           number;
  volumesMonitored:       number;
  volumesCompleted:       number;
  volumesPaused:          number;
  issuesCount:            number;
  issuesDownloaded:       number;
  issuesDownloading:      number;
  issuesMissing:          number;
  totalDownloadedBytes:   number;
  libraries:              DashboardLibraryStats[];
  recentVolumes:          DashboardRecentVolume[];
}

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private http = inject(HttpClient);

  getStats() {
    return this.http.get<DashboardStats>('/api/dashboard/stats');
  }
}
