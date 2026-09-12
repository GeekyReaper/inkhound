import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { VolumeImage } from './volume.service';

// Compteurs établis sur les issues réelles, toutes catégories confondues (et non sur les
// compteurs du Volume, qui ne retiennent que les issues Standard).
export interface DashboardLibraryStats {
  id:                      string;
  name:                    string;
  volumesCount:            number;
  issuesCount:             number;
  downloadedIssuesCount:   number;
  downloadingIssuesCount:  number;
  missingIssuesCount:      number;
}

export interface DashboardRecentVolume {
  id:        string;
  libraryId: string;
  title:     string;
  image:     VolumeImage | null;
  dateAdded: string;
}

export interface DashboardMostWantedIssue {
  issueId:                    string;
  volumeId:                   string;
  libraryId:                  string;
  volumeTitle:                string;
  image:                      VolumeImage | null;
  issueNumber:                number;
  issueTitle:                 string | null;
  ownedCount:                 number;
  totalCount:                 number;
  missingCount:               number;
  currentCompletionPercent:   number;
  projectedCompletionPercent: number;
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
  mostWanted:             DashboardMostWantedIssue[];
}

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private http = inject(HttpClient);

  getStats() {
    return this.http.get<DashboardStats>('/api/dashboard/stats');
  }
}
