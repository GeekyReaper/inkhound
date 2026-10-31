import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { AgeRating, SourceKey } from './volume.service';

export type VolumeRequestStatus = 'Pending' | 'Approved' | 'Rejected';

// Demande d'ajout de volume émise par un Guest — /api/volume-requests.
export interface VolumeRequest {
  id:                string;
  requestedByUserId: string;
  requestedByLogin:  string;
  source:            SourceKey;
  sourceId:          string;
  title:             string;
  year:              number | null;
  image:             string | null;
  libraryId:         string;
  ageRating:         AgeRating;
  status:            VolumeRequestStatus;
  createdAt:         string;
  updatedAt:         string;
  resolvedAt:        string | null;
  volumeId:          string | null;
  /** Job de peuplement des issues — renseigné uniquement à la création par un admin (auto-approuvée). */
  jobId?:            string | null;
}

export interface CreateVolumeRequest {
  libraryId: string;
  source:    SourceKey;
  sourceId:  string;
  title:     string;
  year:      number | null;
  image:     string | null;
  ageRating: AgeRating;
}

export interface UpdateVolumeRequest {
  libraryId?: string;
  ageRating?: AgeRating;
}

@Injectable({ providedIn: 'root' })
export class VolumeRequestService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/volume-requests';

  /** Admin : toutes les demandes ; Guest : les siennes (périmètre imposé par le serveur). */
  getAll(status?: VolumeRequestStatus): Observable<VolumeRequest[]> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    return this.http.get<VolumeRequest[]>(this.base, { params });
  }

  create(request: CreateVolumeRequest): Observable<VolumeRequest> {
    return this.http.post<VolumeRequest>(this.base, request);
  }

  update(id: string, request: UpdateVolumeRequest): Observable<VolumeRequest> {
    return this.http.patch<VolumeRequest>(`${this.base}/${id}`, request);
  }

  approve(id: string): Observable<VolumeRequest> {
    return this.http.post<VolumeRequest>(`${this.base}/${id}/approve`, {});
  }

  reject(id: string): Observable<VolumeRequest> {
    return this.http.post<VolumeRequest>(`${this.base}/${id}/reject`, {});
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
