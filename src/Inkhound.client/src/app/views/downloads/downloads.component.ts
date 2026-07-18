import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval, switchMap, startWith, finalize } from 'rxjs';
import { DatePipe, DecimalPipe } from '@angular/common';
import {
  AlertComponent, BadgeComponent, ButtonDirective,
  CardBodyComponent, CardComponent,
  ColComponent, ContainerComponent,
  ProgressBarComponent, ProgressComponent,
  RowComponent, SpinnerComponent, TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { QBittorrentService, DownloadItem, DownloadStatus } from '../../core/services/qbittorrent.service';
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
    DatePipe, DecimalPipe, IconDirective,
    JobConsoleModalComponent,
  ],
  templateUrl: './downloads.component.html'
})
export class DownloadsComponent implements OnInit {
  private qbService    = inject(QBittorrentService);
  private hub          = inject(HubService);
  readonly #destroyRef = inject(DestroyRef);

  downloads  = signal<DownloadItem[]>([]);
  loading    = signal(true);
  error      = signal<string | null>(null);

  processing    = signal(false);
  processingIds = signal<Set<string>>(new Set());

  selectedJob    = signal<JobContext | null>(null);
  consoleVisible = signal(false);

  readonly activeCount = computed(() =>
    this.downloads().filter(d => d.status === 'Downloading' || d.status === 'Unknown').length
  );
  readonly syncingCount = computed(() =>
    this.downloads().filter(d => d.status === 'Syncing').length
  );
  readonly doneCount = computed(() =>
    this.downloads().filter(d => d.status === 'Done').length
  );
  readonly activeJobs = computed(() =>
    this.hub.jobs().filter(j =>
      (j.state === 'RUNNING' || j.state === 'INITIALIZING') &&
      (j.title === 'Process downloads' || j.title.startsWith('Process download '))
    )
  );

  ngOnInit() {
    interval(10_000)
      .pipe(
        startWith(0),
        switchMap(() => this.qbService.getDownloads()),
        takeUntilDestroyed(this.#destroyRef)
      )
      .subscribe({
        next: items => {
          this.downloads.set(items);
          this.loading.set(false);
          this.error.set(null);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Could not load downloads. Check QBittorrent service configuration.');
        }
      });
  }

  statusBadgeColor(status: DownloadStatus): string {
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
