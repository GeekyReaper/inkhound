import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import {
  AlertComponent,
  BadgeComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  ColComponent,
  ContainerComponent,
  ProgressBarComponent,
  ProgressComponent,
  RowComponent,
  SpinnerComponent,
  TemplateIdDirective,
  TooltipDirective,
  WidgetStatCComponent
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { DashboardService, DashboardStats } from '../../core/services/dashboard.service';
import { HubService } from '../../core/services/hub.service';
import { QBittorrentService, DownloadItem, DownloadStatus } from '../../core/services/qbittorrent.service';
import { VolumeStatus } from '../../core/services/volume.service';

@Component({
  selector: 'app-dashboard',
  templateUrl: './dashboard.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent, BadgeComponent, ButtonDirective,
    ProgressComponent, ProgressBarComponent, TooltipDirective,
    WidgetStatCComponent, TemplateIdDirective, IconDirective, DatePipe, RouterLink
  ]
})
export class DashboardComponent {
  private dashboardService = inject(DashboardService);
  private qbService        = inject(QBittorrentService);
  private hub               = inject(HubService);
  readonly #destroyRef      = inject(DestroyRef);

  private readonly ACTIVE_DOWNLOAD_STATUSES: DownloadStatus[] =
    ['Downloading', 'Paused', 'Finished', 'Syncing', 'Error', 'Unknown'];

  loading = signal(true);
  error   = signal<string | null>(null);
  stats   = signal<DashboardStats | null>(null);

  recentDownloads     = signal<DownloadItem[]>([]);
  recentDownloadsTotal = signal(0);
  downloadsLoading     = signal(true);

  readonly activeJobs = computed(() =>
    this.hub.jobs().filter(j => j.state === 'RUNNING' || j.state === 'INITIALIZING').slice(0, 5)
  );

  readonly issuesProgressPercent = computed(() => {
    const s = this.stats();
    if (!s || !s.issuesCount) return 0;
    return Math.round((s.issuesDownloaded / s.issuesCount) * 100);
  });

  constructor() {
    this.dashboardService.getStats()
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: stats => this.stats.set(stats),
        error: err  => this.error.set(err?.error?.message ?? 'Failed to load dashboard data.')
      });

    this.qbService.getDownloads(this.ACTIVE_DOWNLOAD_STATUSES, 1, 5)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.downloadsLoading.set(false)))
      .subscribe({
        next: res => {
          this.recentDownloads.set(res.items);
          this.recentDownloadsTotal.set(res.totalItems);
        },
        error: () => {}
      });
  }

  libraryProgressPercent(lib: { issuesCount: number; downloadedIssuesCount: number }): number {
    if (!lib.issuesCount) return 0;
    return Math.round((lib.downloadedIssuesCount / lib.issuesCount) * 100);
  }

  volumeStatusBadgeColor(status: VolumeStatus): string {
    const map: Record<VolumeStatus, string> = {
      MONITORED: 'primary',
      COMPLETED: 'success',
      PAUSED:    'secondary'
    };
    return map[status];
  }

  jobProgressColor(state: string): string {
    if (state === 'ERROR')   return 'danger';
    if (state === 'SUCCESS') return 'success';
    return 'primary';
  }

  downloadStatusBadgeColor(status: DownloadStatus): string {
    switch (status) {
      case 'Downloading': return 'info';
      case 'Paused':      return 'warning';
      case 'Finished':    return 'success';
      case 'Syncing':     return 'info';
      case 'Done':        return 'success';
      case 'Error':       return 'danger';
      default:            return 'secondary';
    }
  }

  formatSize(bytes: number | null): string {
    if (bytes === null || bytes <= 0) return '—';
    const mb = bytes / 1_048_576;
    if (mb >= 1_048_576) return `${(mb / 1_048_576).toFixed(1)} TB`;
    return mb >= 1000 ? `${(mb / 1024).toFixed(1)} GB` : `${mb.toFixed(0)} MB`;
  }
}
