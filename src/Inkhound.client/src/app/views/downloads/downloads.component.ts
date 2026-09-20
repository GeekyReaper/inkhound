import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { interval, merge, switchMap, finalize } from 'rxjs';
import { DecimalPipe } from '@angular/common';
import {
  AlertComponent, ButtonDirective,
  CardBodyComponent, CardComponent,
  ColComponent, ContainerComponent,
  PageItemComponent, PageLinkDirective, PaginationComponent,
  ProgressBarComponent, ProgressComponent,
  RowComponent, SpinnerComponent
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { QBittorrentService, DownloadStatus, DownloadsPageResult } from '../../core/services/qbittorrent.service';
import { HubService } from '../../core/services/hub.service';
import { JobContext } from '../../core/models/hub.models';
import { JobConsoleModalComponent } from '../job-console-modal/job-console-modal.component';
import { DownloadListComponent } from '../download-list/download-list.component';

@Component({
  selector: 'app-downloads',
  standalone: true,
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent, ButtonDirective,
    ProgressComponent, ProgressBarComponent,
    PaginationComponent, PageItemComponent, PageLinkDirective,
    DecimalPipe, IconDirective,
    JobConsoleModalComponent, DownloadListComponent
  ],
  templateUrl: './downloads.component.html'
})
export class DownloadsComponent {
  private qbService    = inject(QBittorrentService);
  private hub          = inject(HubService);
  readonly #destroyRef = inject(DestroyRef);

  // "Active" regroupe tout ce qui n'est pas encore terminé/rangé ; "Done" est à part.
  readonly ACTIVE_STATUSES: DownloadStatus[] =
    ['Downloading', 'Stalled', 'Paused', 'Finished', 'Syncing', 'Error', 'Unknown', 'NotFound'];
  readonly DONE_STATUSES: DownloadStatus[] = ['Done'];

  loading = signal(true);
  error   = signal<string | null>(null);

  processing    = signal(false);
  processingIds = signal<Set<string>>(new Set());
  refreshing    = signal(false);

  selectedJob    = signal<JobContext | null>(null);
  consoleVisible = signal(false);

  // Le rendu d'une ligne, ses actions et les modales (fix hash / suppression) vivent dans
  // app-download-list, partagé avec la page Issue.

  // Choix exclusif — Active par défaut (Done, déjà traités, n'a que peu d'intérêt à être vu en
  // premier).
  filterMode = signal<'active' | 'done'>('active');

  readonly pageSize   = 20;
  currentPage         = signal(1);
  pageResult          = signal<DownloadsPageResult | null>(null);
  readonly items      = computed(() => this.pageResult()?.items ?? []);
  // Bumper ce signal force un refetch immédiat (après une suppression, sans attendre le poll 10 s).
  private reloadTick  = signal(0);

  readonly visiblePages = computed(() => {
    const total   = this.pageResult()?.totalPages ?? 0;
    const current = this.pageResult()?.pageNumber ?? 1;
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
    const start = Math.max(1, Math.min(current - 3, total - 6));
    const end   = Math.min(total, start + 6);
    return Array.from({ length: end - start + 1 }, (_, i) => start + i);
  });

  private readonly fetchParams = computed(() => ({
    statuses: this.filterMode() === 'active' ? this.ACTIVE_STATUSES : this.DONE_STATUSES,
    page: this.currentPage(),
    tick: this.reloadTick()
  }));

  readonly activeJobs = computed(() =>
    this.hub.jobs().filter(j =>
      (j.state === 'RUNNING' || j.state === 'INITIALIZING') &&
      (j.title === 'Process downloads' || j.title.startsWith('Process download ') || j.title === 'Refresh downloads')
    )
  );

  constructor() {
    // toObservable() doit être appelé dans un contexte d'injection (constructeur/field
    // initializer) — pas dans ngOnInit(), d'où le déplacement de toute cette logique ici.
    // Il émet immédiatement avec la valeur courante (chargement initial), puis à chaque
    // changement de filtre/page — merge avec le poll périodique pour l'auto-refresh.
    merge(interval(10_000), toObservable(this.fetchParams))
      .pipe(
        switchMap(() => {
          const { statuses, page } = this.fetchParams();
          return this.qbService.getDownloads(statuses, page, this.pageSize);
        }),
        takeUntilDestroyed(this.#destroyRef)
      )
      .subscribe({
        next: res => {
          this.pageResult.set(res);
          this.loading.set(false);
          this.error.set(null);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Could not load downloads. Check QBittorrent service configuration.');
        }
      });
  }

  setFilterMode(mode: 'active' | 'done'): void {
    if (this.filterMode() === mode) return;
    this.filterMode.set(mode);
    this.currentPage.set(1);
  }

  goToPage(page: number): void {
    if (page < 1 || page > (this.pageResult()?.totalPages ?? 1)) return;
    this.currentPage.set(page);
  }

  openConsole(job: JobContext): void {
    this.selectedJob.set(job);
    this.consoleVisible.set(true);
  }

  processAll(): void {
    if (this.processing()) return;
    this.processing.set(true);
    this.qbService.processDownloads()
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.processing.set(false)))
      .subscribe({ next: () => {}, error: () => {} });
  }

  refreshDownloads(): void {
    if (this.refreshing()) return;
    this.refreshing.set(true);
    this.qbService.refreshDownloads()
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.refreshing.set(false)))
      .subscribe({ next: () => {}, error: () => {} });
  }

  // Refetch immédiat après une action de app-download-list (suppression, import, hash corrigé),
  // sans attendre le poll 10 s.
  reload(): void {
    this.reloadTick.update(t => t + 1);
  }
}
