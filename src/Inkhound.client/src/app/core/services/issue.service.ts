import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { VolumeAuthor, VolumeImage } from './volume.service';

export type IssueStatus = 'SEEKING' | 'DOWNLOADING' | 'DOWNLOADED' | 'MISSING';

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

  getByVolume(volumeId: string) {
    return this.http.get<Issue[]>(`/api/volumes/${volumeId}/issues`);
  }
}
