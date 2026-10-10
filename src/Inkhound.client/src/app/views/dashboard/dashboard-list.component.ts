import { Component, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, forkJoin } from 'rxjs';
import {
  AlertComponent, ButtonDirective, ColComponent, ContainerComponent, RowComponent, SpinnerComponent
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import {
  DashboardMostWantedIssue, DashboardRecentVolume, DashboardService
} from '../../core/services/dashboard.service';
import { QBittorrentService, DownloadItem, DownloadStatus } from '../../core/services/qbittorrent.service';
import { DownloadCardComponent } from '../download-card/download-card.component';
import { MostWantedCardComponent } from './cards/most-wanted-card.component';
import { RecentVolumeCardComponent } from './cards/recent-volume-card.component';

type ListKind = 'recently-added' | 'most-wanted' | 'downloads';

const TITLES: Record<ListKind, string> = {
  'recently-added': 'Recently added',
  'most-wanted':    'Most wanted',
  'downloads':      'Downloads'
};

// Même périmètre que la carte Downloads du Dashboard ; 'Stalled' est servi par son propre endpoint.
const ACTIVE_DOWNLOAD_STATUSES: DownloadStatus[] =
  ['Downloading', 'Paused', 'Finished', 'Syncing', 'Error', 'Unknown'];

// Sous-pages du Dashboard : « Recently added », « Most wanted » et « Downloads » (bloqués puis en cours), 50 entrées chacune.
// Le type est porté par `data.kind` de la route.
@Component({
  selector: 'app-dashboard-list',
  standalone: true,
  imports: [
    ContainerComponent, RowComponent, ColComponent, SpinnerComponent, AlertComponent, ButtonDirective,
    IconDirective, RecentVolumeCardComponent, MostWantedCardComponent, DownloadCardComponent
  ],
  templateUrl: './dashboard-list.component.html'
})
export class DashboardListComponent {
  private dashboardService = inject(DashboardService);
  private qbService        = inject(QBittorrentService);
  private router           = inject(Router);
  readonly #destroyRef     = inject(DestroyRef);

  static readonly LIMIT = 50;

  readonly kind  = inject(ActivatedRoute).snapshot.data['kind'] as ListKind;
  readonly title = TITLES[this.kind];

  loading = signal(true);
  error   = signal<string | null>(null);
  recent  = signal<DashboardRecentVolume[]>([]);
  wanted  = signal<DashboardMostWantedIssue[]>([]);
  downloads = signal<DownloadItem[]>([]);

  goBack(): void {
    this.router.navigate(['/dashboard']);
  }

  constructor() {
    const limit = DashboardListComponent.LIMIT;
    const onError = (err: { error?: { message?: string } }) =>
      this.error.set(err?.error?.message ?? 'Failed to load the list.');

    if (this.kind === 'downloads') {
      forkJoin({
        stalled: this.qbService.getStalledDownloads(limit),
        active:  this.qbService.getDownloads(ACTIVE_DOWNLOAD_STATUSES, 1, limit)
      })
        .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.loading.set(false)))
        .subscribe({
          next: ({ stalled, active }) => this.downloads.set([...stalled, ...active.items].slice(0, limit)),
          error: onError
        });
    } else if (this.kind === 'most-wanted') {
      this.dashboardService.getMostWanted(limit)
        .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.loading.set(false)))
        .subscribe({ next: items => this.wanted.set(items), error: onError });
    } else {
      this.dashboardService.getRecentVolumes(limit)
        .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.loading.set(false)))
        .subscribe({ next: items => this.recent.set(items), error: onError });
    }
  }
}
