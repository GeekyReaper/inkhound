import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval, switchMap, startWith } from 'rxjs';
import { DatePipe, DecimalPipe } from '@angular/common';
import {
  AlertComponent, BadgeComponent, ButtonDirective,
  CardBodyComponent, CardComponent,
  ColComponent, ContainerComponent,
  ProgressBarComponent, ProgressComponent,
  RowComponent, SpinnerComponent, TableDirective
} from '@coreui/angular';
import { QBittorrentService, DownloadItem, DownloadStatus } from '../../core/services/qbittorrent.service';

@Component({
  selector: 'app-downloads',
  standalone: true,
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent, BadgeComponent, ButtonDirective,
    TableDirective, ProgressComponent, ProgressBarComponent,
    DatePipe, DecimalPipe
  ],
  templateUrl: './downloads.component.html'
})
export class DownloadsComponent implements OnInit {
  private qbService    = inject(QBittorrentService);
  readonly #destroyRef = inject(DestroyRef);

  downloads  = signal<DownloadItem[]>([]);
  loading    = signal(true);
  error      = signal<string | null>(null);

  readonly activeCount = computed(() =>
    this.downloads().filter(d => d.status === 'Downloading' || d.status === 'Unknown').length
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
      case 'Error':       return 'danger';
      default:            return 'secondary';
    }
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
