import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { VolumeAuthor, VolumeImage, PageResult } from './volume.service';

export type IssueStatus = 'DOWNLOADING' | 'DOWNLOADED' | 'MISSING';

export interface ComicVineIssue {
  id:            string;
  issueNumber:   string | null;
  name:          string | null;
  coverDate:     string | null;
  thumbUrl:      string | null;
  siteDetailUrl: string | null;
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
  comicVineId: string;
  issueNumber: number;
  title:       string | null;
  year:        number | null;
  description: string | null;
  status:      IssueStatus;
  authors:     VolumeAuthor[];
  image:       VolumeImage | null;
  cbzFilename: string | null;
  publishedAt: string | null;
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

  getByComicVineVolume(cvVolumeId: string, page = 1, pageSize = 10) {
    return this.http.get<PageResult<ComicVineIssue>>(`/api/issues/comicvine`, {
      params: { comicVineVolumeId: cvVolumeId, page, pageSize }
    });
  }

  update(issueId: string, request: UpdateIssueManuallyRequest) {
    return this.http.put<void>(`/api/issues/${issueId}`, request);
  }
}
