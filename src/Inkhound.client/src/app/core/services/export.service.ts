import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, tap } from 'rxjs';
import { OptionsService } from './options.service';

export type ExportTargetType = 'Issue' | 'Volume';
export type ExportFormat = 'Pdf' | 'Cbz';
export type ExportStatus = 'Pending' | 'Ready';

export interface ExportFile {
  id: string;
  targetType: ExportTargetType;
  targetId: string;
  /** Format des issues (pour un volume : format des fichiers contenus dans le ZIP). */
  format: ExportFormat;
  fileName: string;
  sizeBytes: number;
  createdAt: string;
  /** Date de suppression prévue ; null si le nettoyage planifié est désactivé. */
  expiresAt: string | null;
  status: ExportStatus;
  jobId: string | null;
}

@Injectable({ providedIn: 'root' })
export class ExportService {
  private http    = inject(HttpClient);
  private options = inject(OptionsService);

  exportIssue(issueId: string, format: ExportFormat) {
    return this.http.post<{ jobId: string }>(`/api/issues/${issueId}/export`, { format });
  }

  exportVolume(volumeId: string, format: ExportFormat) {
    return this.http.post<{ jobId: string }>(`/api/volumes/${volumeId}/export`, { format });
  }

  list(targetType: ExportTargetType, targetId: string) {
    return this.http.get<ExportFile[]>('/api/exports', { params: { targetType, targetId } });
  }

  delete(id: string) {
    return this.http.delete<void>(`/api/exports/${id}`);
  }

  /** Format préselectionné, tel que réglé dans le module Export (Settings). */
  getDefaultFormat(): Observable<ExportFormat> {
    return this.options.getOptions('Export').pipe(
      map(defs => (defs.find(d => d.name === 'DefaultFormat')?.value === 'Pdf' ? 'Pdf' : 'Cbz') as ExportFormat)
    );
  }

  /**
   * Lance le téléchargement : un lien natif ne porte pas le JWT, on demande donc d'abord un ticket
   * à usage unique (authentifié) puis on ouvre son URL — le navigateur télécharge en streaming,
   * sans charger le fichier en mémoire.
   */
  download(id: string): Observable<void> {
    return this.http.post<{ url: string }>(`/api/exports/${id}/ticket`, null).pipe(
      tap(res => {
        const link = document.createElement('a');
        link.href = res.url;
        link.download = '';
        document.body.appendChild(link);
        link.click();
        link.remove();
      }),
      map(() => undefined)
    );
  }
}
