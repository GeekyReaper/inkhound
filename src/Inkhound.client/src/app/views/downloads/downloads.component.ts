import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { interval, merge, switchMap, finalize } from 'rxjs';
import { DatePipe, DecimalPipe } from '@angular/common';
import {
  AlertComponent, BadgeComponent, ButtonCloseDirective, ButtonDirective,
  CardBodyComponent, CardComponent,
  ColComponent, ContainerComponent,
  FormControlDirective, FormLabelDirective,
  ModalBodyComponent, ModalComponent, ModalFooterComponent, ModalHeaderComponent, ModalTitleDirective,
  PageItemComponent, PageLinkDirective, PaginationComponent,
  ProgressBarComponent, ProgressComponent,
  RowComponent, SpinnerComponent, TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { QBittorrentService, DownloadItem, DownloadStatus, DownloadsPageResult } from '../../core/services/qbittorrent.service';
import { HubService } from '../../core/services/hub.service';
import { JobContext } from '../../core/models/hub.models';
import { JobConsoleModalComponent } from '../job-console-modal/job-console-modal.component';

@Component({
  selector: 'app-downloads',
  standalone: true,
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent, BadgeComponent, ButtonDirective,
    TableDirective, ProgressComponent, ProgressBarComponent,
    PaginationComponent, PageItemComponent, PageLinkDirective,
    DatePipe, DecimalPipe, IconDirective,
    JobConsoleModalComponent,
    ModalComponent, ModalHeaderComponent, ModalBodyComponent, ModalFooterComponent,
    ModalTitleDirective, ButtonCloseDirective, FormControlDirective, FormLabelDirective,
  ],
  templateUrl: './downloads.component.html'
})
export class DownloadsComponent {
  private qbService    = inject(QBittorrentService);
  private hub          = inject(HubService);
  readonly #destroyRef = inject(DestroyRef);

  // "Active" regroupe tout ce qui n'est pas encore terminé/rangé ; "Done" est à part.
  readonly ACTIVE_STATUSES: DownloadStatus[] =
    ['Downloading', 'Paused', 'Finished', 'Syncing', 'Error', 'Unknown'];
  readonly DONE_STATUSES: DownloadStatus[] = ['Done'];

  loading = signal(true);
  error   = signal<string | null>(null);

  processing    = signal(false);
  processingIds = signal<Set<string>>(new Set());
  refreshing    = signal(false);

  selectedJob    = signal<JobContext | null>(null);
  consoleVisible = signal(false);

  // --- État de la modale d'édition (correction de hash / suppression) ---
  editModalVisible = signal(false);
  editingItem       = signal<DownloadItem | null>(null);
  editHashInput     = signal('');
  editSaving        = signal(false);
  editError         = signal<string | null>(null);
  confirmingDelete  = signal(false);
  deleting          = signal(false);

  // Choix exclusif — Active par défaut (Done, déjà traités, n'a que peu d'intérêt à être vu en
  // premier).
  filterMode = signal<'active' | 'done'>('active');

  readonly pageSize   = 20;
  currentPage         = signal(1);
  pageResult          = signal<DownloadsPageResult | null>(null);
  readonly items      = computed(() => this.pageResult()?.items ?? []);

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
    page: this.currentPage()
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

  statusBadgeColor(status: DownloadStatus): string {
    switch (status) {
      case 'Downloading': return 'info';
      case 'Paused':      return 'warning';
      case 'Finished':    return 'success';
      case 'Syncing':     return 'info';
      case 'Done':        return 'success';
      case 'Error':       return 'danger';
      case 'NotFound':    return 'dark';
      default:            return 'secondary';
    }
  }

  canProcess(item: DownloadItem): boolean {
    return item.status === 'Finished' || item.status === 'Syncing';
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

  processOne(item: DownloadItem): void {
    if (this.processingIds().has(item.id)) return;
    this.processingIds.update(ids => new Set(ids).add(item.id));
    this.qbService.processDownload(item.id)
      .pipe(
        takeUntilDestroyed(this.#destroyRef),
        finalize(() => this.processingIds.update(ids => {
          const next = new Set(ids);
          next.delete(item.id);
          return next;
        }))
      )
      .subscribe({ next: () => {}, error: () => {} });
  }

  // --- Modale d'édition (ligne NotFound : corriger le hash ou supprimer le suivi) ---

  openEditModal(item: DownloadItem): void {
    this.editingItem.set(item);
    this.editHashInput.set('');
    this.editError.set(null);
    this.confirmingDelete.set(false);
    this.editModalVisible.set(true);
  }

  onEditModalVisibleChange(visible: boolean): void {
    if (!visible) this.closeEditModal();
  }

  closeEditModal(): void {
    this.editModalVisible.set(false);
    this.editingItem.set(null);
    this.editHashInput.set('');
    this.editError.set(null);
    this.confirmingDelete.set(false);
  }

  saveHash(): void {
    const item = this.editingItem();
    const hash = this.editHashInput().trim();
    if (!item || !hash || this.editSaving()) return;

    this.editSaving.set(true);
    this.editError.set(null);
    this.qbService.updateDownloadHash(item.id, hash)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.editSaving.set(false)))
      .subscribe({
        next:  () => this.closeEditModal(),
        error: err => this.editError.set(err?.error?.message ?? 'Failed to update hash.')
      });
  }

  requestDelete(): void {
    this.editError.set(null);
    this.confirmingDelete.set(true);
  }

  confirmDelete(): void {
    const item = this.editingItem();
    if (!item || this.deleting()) return;

    this.deleting.set(true);
    this.editError.set(null);
    this.qbService.deleteDownload(item.id)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.deleting.set(false)))
      .subscribe({
        next:  () => this.closeEditModal(),
        error: err => this.editError.set(err?.error?.message ?? 'Failed to delete download.')
      });
  }

  formatSpeed(bytesPerSec: number | null): string {
    if (bytesPerSec === null || bytesPerSec <= 0) return '—';
    if (bytesPerSec >= 1_048_576) return `${(bytesPerSec / 1_048_576).toFixed(1)} MB/s`;
    return `${(bytesPerSec / 1024).toFixed(0)} KB/s`;
  }

  formatEta(seconds: number | null): string {
    if (seconds === null || seconds <= 0 || seconds >= 8640000) return '—';
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = seconds % 60;
    if (h > 0) return `${h}h ${m}m`;
    if (m > 0) return `${m}m ${s}s`;
    return `${s}s`;
  }

  formatSize(bytes: number | null): string {
    if (bytes === null || bytes <= 0) return '—';
    const mb = bytes / 1_048_576;
    return mb >= 1000 ? `${(mb / 1024).toFixed(1)} GB` : `${mb.toFixed(0)} MB`;
  }
}
