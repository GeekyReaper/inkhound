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
  ProgressStackedComponent,
  RowComponent,
  SpinnerComponent,
  TemplateIdDirective,
  TooltipDirective,
  WidgetStatCComponent
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { DashboardService, DashboardStats, DashboardMostWantedIssue, DashboardLibraryStats } from '../../core/services/dashboard.service';
import { HubService } from '../../core/services/hub.service';
import { QBittorrentService, DownloadItem, DownloadStatus } from '../../core/services/qbittorrent.service';
import { VolumeStatus } from '../../core/services/volume.service';
import { downloadStatusColor, formatSize } from '../../core/util/download-format';

@Component({
  selector: 'app-dashboard',
  templateUrl: './dashboard.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent, BadgeComponent, ButtonDirective,
    ProgressComponent, ProgressBarComponent, ProgressStackedComponent, TooltipDirective,
    WidgetStatCComponent, TemplateIdDirective, IconDirective, DatePipe, RouterLink
  ]
})
export class DashboardComponent {
  private dashboardService = inject(DashboardService);
  private qbService        = inject(QBittorrentService);
  private hub               = inject(HubService);
  readonly #destroyRef      = inject(DestroyRef);

  private readonly ACTIVE_DOWNLOAD_STATUSES: DownloadStatus[] =
    ['Downloading', 'Stalled', 'Paused', 'Finished', 'Syncing', 'Error', 'Unknown'];

  loading = signal(true);
  error   = signal<string | null>(null);
  stats   = signal<DashboardStats | null>(null);

  recentDownloads     = signal<DownloadItem[]>([]);
  recentDownloadsTotal = signal(0);
  downloadsLoading     = signal(true);

  // Téléchargements bloqués (aucune source) : ils n'avanceront jamais seuls — section d'alerte,
  // masquée tant que la liste est vide.
  stalledDownloads = signal<DownloadItem[]>([]);

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

    this.qbService.getStalledDownloads(5)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  items => this.stalledDownloads.set(items),
        error: ()    => { /* section d'alerte : un échec la laisse simplement masquée */ }
      });
  }

  // Segments de la barre d'une bibliothèque : un par statut d'issue. Missing est volontairement en
  // gris neutre plutôt que dans le rouge du badge Missing (cf. IssueCardComponent) : il domine la
  // barre sur une bibliothèque encore incomplète, et un aplat rouge s'y lirait comme une alerte.
  // Les statuts absents sont retirés pour ne pas produire de segment de largeur nulle. Les
  // pourcentages ne sont pas arrondis : trois arrondis indépendants dépasseraient les 100 % et
  // décaleraient la barre.
  librarySegments(lib: DashboardLibraryStats): { label: string; count: number; color: string; percent: number }[] {
    const total = lib.issuesCount;
    if (!total) return [];

    return [
      { label: 'Downloaded',  count: lib.downloadedIssuesCount,  color: 'success' },
      { label: 'Downloading', count: lib.downloadingIssuesCount, color: 'info' },
      { label: 'Missing',     count: lib.missingIssuesCount,     color: 'secondary' }
    ]
      .filter(segment => segment.count > 0)
      .map(segment => ({ ...segment, percent: (segment.count / total) * 100 }));
  }

  libraryTooltip(lib: DashboardLibraryStats): string {
    return `${lib.downloadedIssuesCount} downloaded / ${lib.downloadingIssuesCount} downloading `
         + `/ ${lib.missingIssuesCount} missing — ${lib.issuesCount} total`;
  }

  mostWantedCover(item: DashboardMostWantedIssue): string | null {
    return item.image?.smallUrl ?? item.image?.thumbUrl ?? null;
  }

  mostWantedGainPercent(item: DashboardMostWantedIssue): number {
    return item.projectedCompletionPercent - item.currentCompletionPercent;
  }

  mostWantedTooltip(item: DashboardMostWantedIssue): string {
    return `${item.ownedCount} / ${item.totalCount} owned · ${item.missingCount} missing`;
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

  // Helpers partagés avec la page Downloads (core/util/download-format.ts).
  readonly downloadStatusBadgeColor = downloadStatusColor;
  readonly formatSize = formatSize;

  // Lien vers la page de l'issue bloquée — null si le volume/la library parents ne sont pas connus
  // (entité supprimée entre-temps) : la ligne reste affichée, simplement non cliquable.
  stalledLink(item: DownloadItem): unknown[] | null {
    if (!item.volumeId || !item.libraryId) return null;
    return ['/library', item.libraryId, 'volume', item.volumeId, 'issue', item.issueId];
  }
}
