import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
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
import { DashboardService, DashboardStats, DashboardLibraryStats } from '../../core/services/dashboard.service';
import { HubService } from '../../core/services/hub.service';
import { QBittorrentService, DownloadItem, DownloadStatus } from '../../core/services/qbittorrent.service';
import { VolumeStatus } from '../../core/services/volume.service';
import { formatSize } from '../../core/util/download-format';
import { DownloadCardComponent } from '../download-card/download-card.component';
import { MostWantedCardComponent } from './cards/most-wanted-card.component';
import { RecentVolumeCardComponent } from './cards/recent-volume-card.component';

@Component({
  selector: 'app-dashboard',
  templateUrl: './dashboard.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent, BadgeComponent, ButtonDirective,
    ProgressComponent, ProgressBarComponent, ProgressStackedComponent, TooltipDirective,
    WidgetStatCComponent, TemplateIdDirective, IconDirective, RouterLink,
    DownloadCardComponent, MostWantedCardComponent, RecentVolumeCardComponent
  ]
})
export class DashboardComponent {
  private dashboardService = inject(DashboardService);
  private qbService        = inject(QBittorrentService);
  private hub               = inject(HubService);
  readonly #destroyRef      = inject(DestroyRef);

  // 'Stalled' est volontairement exclu : ces téléchargements ont leur propre section d'alerte
  // juste au-dessus, les lister deux fois en cartes ferait doublon à l'écran.
  private readonly ACTIVE_DOWNLOAD_STATUSES: DownloadStatus[] =
    ['Downloading', 'Paused', 'Finished', 'Syncing', 'Error', 'Unknown'];

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

  // Carte « Downloads » : les bloqués d'abord (alerte), puis les téléchargements en cours.
  readonly dashboardDownloads = computed(() =>
    [...this.stalledDownloads(), ...this.recentDownloads()].slice(0, 6));
  readonly downloadsTotal = computed(() => this.stalledDownloads().length + this.recentDownloadsTotal());

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

  // Helper partagé avec la page Downloads (core/util/download-format.ts). Le statut, le lien vers
  // l'issue et la couleur du badge d'un téléchargement vivent dans app-download-card.
  readonly formatSize = formatSize;
}
