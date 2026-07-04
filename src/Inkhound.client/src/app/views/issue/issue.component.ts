import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { filter, finalize, switchMap } from 'rxjs';
import { DatePipe, DecimalPipe } from '@angular/common';
import {
  AlertComponent, BadgeComponent, ButtonDirective,
  CardBodyComponent, CardComponent,
  ColComponent, ContainerComponent,
  ProgressBarComponent, ProgressComponent,
  RowComponent, SpinnerComponent, TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { Issue, IssueService, IssueStatus } from '../../core/services/issue.service';
import { ProwlarrCategory, ProwlarrService, ScoredSearchResult } from '../../core/services/prowlarr.service';
import { QBittorrentService } from '../../core/services/qbittorrent.service';
import { HubService } from '../../core/services/hub.service';
import { UpdatedData } from '../../core/models/hub.models';

@Component({
  selector: 'app-issue',
  standalone: true,
  templateUrl: './issue.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent, ButtonDirective, BadgeComponent, IconDirective,
    TableDirective, ProgressComponent, ProgressBarComponent,
    DatePipe, DecimalPipe
  ]
})
export class IssueComponent {
  private route             = inject(ActivatedRoute);
  private router            = inject(Router);
  private issueService      = inject(IssueService);
  private prowlarrService   = inject(ProwlarrService);
  private qbittorrentService = inject(QBittorrentService);
  private hub               = inject(HubService);
  readonly #destroyRef    = inject(DestroyRef);

  readonly issueId = this.route.snapshot.paramMap.get('issueId')!;

  issue         = signal<Issue | null>(null);
  loading       = signal(true);
  loadError     = signal<string | null>(null);
  searching     = signal(false);
  searchError   = signal<string | null>(null);
  searchResults = signal<ScoredSearchResult[]>([]);
  grabbingGuid  = signal<string | null>(null);
  grabSuccess   = signal<string | null>(null);
  currentPage   = signal(1);

  readonly pageSize     = 50;
  readonly totalPages   = computed(() => Math.ceil(this.searchResults().length / this.pageSize));
  readonly pageNumbers  = computed(() => Array.from({ length: this.totalPages() }, (_, i) => i + 1));
  readonly pagedResults = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize;
    return this.searchResults().slice(start, start + this.pageSize);
  });

  readonly currentJob = this.hub.currentJob;

  constructor() {
    this.issueService.getById(this.issueId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  issue => { this.issue.set(issue); this.loading.set(false); },
        error: ()    => { this.loadError.set('Issue not found.'); this.loading.set(false); }
      });

    toObservable(this.hub.lastDataUpdated)
      .pipe(
        filter((d): d is UpdatedData => d !== null && d.dataType.endsWith('Issue') && d.id === this.issueId),
        switchMap(() => this.issueService.getById(this.issueId)),
        takeUntilDestroyed(this.#destroyRef)
      )
      .subscribe(issue => this.issue.set(issue));
  }

  goBack(): void {
    this.router.navigate(['.'], { relativeTo: this.route.parent });
  }

  onSearch(): void {
    this.searching.set(true);
    this.searchResults.set([]);
    this.searchError.set(null);
    this.grabSuccess.set(null);

    this.prowlarrService.searchForIssue(this.issueId)
      .pipe(
        takeUntilDestroyed(this.#destroyRef),
        finalize(() => this.searching.set(false))
      )
      .subscribe({
        next:  results => { this.searchResults.set(results); this.currentPage.set(1); },
        error: err     => this.searchError.set(err?.error?.message ?? 'Search failed.')
      });
  }

  onGrab(result: ScoredSearchResult): void {
    if (this.grabbingGuid() !== null) return;
    this.grabbingGuid.set(result.result.guid);
    this.grabSuccess.set(null);
    this.searchError.set(null);

    const isTorrent = result.result.protocol?.toLowerCase() === 'torrent';

    if (isTorrent) {
      if (!result.result.downloadUrl) {
        this.searchError.set('No download URL available for this torrent.');
        this.grabbingGuid.set(null);
        return;
      }
      this.qbittorrentService.grab(result.result.downloadUrl, this.issueId)
        .pipe(
          takeUntilDestroyed(this.#destroyRef),
          finalize(() => this.grabbingGuid.set(null))
        )
        .subscribe({
          next:  () => this.grabSuccess.set(`"${result.result.title}" sent to QBittorrent.`),
          error: err => this.searchError.set(err?.error?.message ?? 'Grab failed.')
        });
    } else {
      this.prowlarrService.grab(result.result.guid, result.result.indexerId, this.issueId)
        .pipe(
          takeUntilDestroyed(this.#destroyRef),
          finalize(() => this.grabbingGuid.set(null))
        )
        .subscribe({
          next:  () => this.grabSuccess.set(`"${result.result.title}" sent to download client.`),
          error: err => this.searchError.set(err?.error?.message ?? 'Grab failed.')
        });
    }
  }

  formatSize(bytes: number): string {
    if (bytes <= 0) return '—';
    const mb = bytes / 1_048_576;
    return mb >= 1000 ? `${(mb / 1024).toFixed(1)} GB` : `${mb.toFixed(0)} MB`;
  }

  detectFormat(title: string): string {
    const t = title.toLowerCase();
    if (t.includes('cbz')) return 'CBZ';
    if (t.includes('cbr')) return 'CBR';
    if (t.includes('pdf')) return 'PDF';
    return '—';
  }

  scoreColor(score: number): string {
    if (score >= 70) return 'success';
    if (score >= 40) return 'warning';
    return 'danger';
  }

  categoryNames(categories: ProwlarrCategory[]): string {
    return categories.map(c => c.name).join(', ');
  }

  prevPage(): void { if (this.currentPage() > 1) this.currentPage.update(p => p - 1); }
  nextPage(): void { if (this.currentPage() < this.totalPages()) this.currentPage.update(p => p + 1); }

  issueStatusBadgeClass(status: IssueStatus): string {
    const map: Record<IssueStatus, string> = {
      DOWNLOADING: 'badge bg-info text-dark',
      DOWNLOADED:  'badge bg-success',
      MISSING:     'badge bg-danger'
    };
    return map[status];
  }
}
