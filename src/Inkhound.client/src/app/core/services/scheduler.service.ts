import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

// Clés de tâche acceptées par POST /api/scheduler/run/{key} (cf. InkhoundManager).
export type SchedulerTaskKey = 'ProcessDownloads' | 'RollingRefresh' | 'AutoSearch';

export interface SchedulerTaskStatus {
  enabled: boolean;
  cron: string;
  lastRunUtc: string | null;
  nextRunUtc: string | null;
  running: boolean;
}

export interface SchedulerStatus {
  processDownloads: SchedulerTaskStatus;
  rollingRefresh: SchedulerTaskStatus;
  rollingRefreshBatchSize: number;
  autoSearch: SchedulerTaskStatus;
  autoSearchBatchSize: number;
  autoSearchMinScore: number;
}

export interface SchedulerConfigRequest {
  processDownloadsEnabled: boolean;
  processDownloadsCron: string;
  rollingRefreshEnabled: boolean;
  rollingRefreshCron: string;
  rollingRefreshBatchSize: number;
  autoSearchEnabled: boolean;
  autoSearchCron: string;
  autoSearchBatchSize: number;
  autoSearchMinScore: number;
}

@Injectable({ providedIn: 'root' })
export class SchedulerService {
  private http = inject(HttpClient);

  get() {
    return this.http.get<SchedulerStatus>('/api/scheduler');
  }

  update(request: SchedulerConfigRequest) {
    return this.http.put<SchedulerStatus>('/api/scheduler', request);
  }

  runNow(key: SchedulerTaskKey) {
    return this.http.post<{ message: string }>(`/api/scheduler/run/${key}`, null);
  }
}
