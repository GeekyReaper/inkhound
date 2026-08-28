import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { VolumeAuthor, VolumeImage, PageResult, SourceKey } from './volume.service';

export type IssueStatus = 'DOWNLOADING' | 'DOWNLOADED' | 'MISSING';

export interface SourceIssue {
  sourceId:   string;
  source:     SourceKey;
  name:       string | null;
  issueNumber: string;
  coverDate:  string | null;
  imageUrl:   string | null;
  siteUrl:    string | null;
}

export interface UpdateIssueManuallyRequest {
  title:       string | null;
  year:        number | null;
  description: string | null;
  status:      IssueStatus;
}

export interface Issue {
  id:          string;
  volumeId:    string;
  sourceId:    string;
  issueNumber: number;
  title:       string | null;
  year:        number | null;
  description: string | null;
  status:      IssueStatus;
  authors:     VolumeAuthor[];
  image:       VolumeImage | null;
  cbzFilename: string | null;
  publishedAt: string | null;
  ean:                    string | null;
  collection:              string | null;
  publisher:               string | null;
  legalDepositDate:        string | null;
  officialPageCount:       number | null;
  genre:                   string | null;
  communityRating:         number | null;
  communityRatingCount:    number | null;
  analysisScore:                     number | null;
  analysisScoreBand:                 string | null;
  analysisDominantImageFormat:       string | null;
  analysisDominantResolutionWidth:   number | null;
  analysisDominantResolutionHeight:  number | null;
  analysisPageCount:                 number | null;
  analysisHasComicInfo:              boolean | null;
  analysisZipCompressionPercent:     number | null;
  analysisFileSizeBytes:             number | null;
  analysisAveragePageSizeBytes:      number | null;
  analysisFileHash:                  string | null;
  analyzedAt:                        string | null;
}

@Injectable({ providedIn: 'root' })
export class IssueService {
  private http = inject(HttpClient);

  getById(issueId: string) {
    return this.http.get<Issue>(`/api/issues/${issueId}`);
  }

  getByVolume(volumeId: string) {
    return this.http.get<Issue[]>(`/api/volumes/${volumeId}/issues`);
  }

  getBySourceVolume(source: SourceKey, sourceVolumeId: string, page = 1, pageSize = 10) {
    return this.http.get<PageResult<SourceIssue>>(`/api/issues/source`, {
      params: { source, sourceVolumeId, page, pageSize }
    });
  }

  update(issueId: string, request: UpdateIssueManuallyRequest) {
    return this.http.put<void>(`/api/issues/${issueId}`, request);
  }

  // Lance l'analyse CBZ comme un Job et retourne son jobId. L'Issue mise à jour arrive ensuite
  // via l'abonnement à ManagerDataUpdated (HubService.lastDataUpdated), pas via cette réponse.
  analyze(issueId: string) {
    return this.http.post<{ jobId: string }>(`/api/issues/${issueId}/analyze`, null);
  }
}
