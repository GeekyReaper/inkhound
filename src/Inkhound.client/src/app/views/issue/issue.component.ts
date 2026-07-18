import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { filter, finalize, switchMap } from 'rxjs';
import { DatePipe, DecimalPipe, SlicePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AlertComponent, BadgeComponent, ButtonDirective,
  CardBodyComponent, CardComponent,
  ColComponent, ContainerComponent,
  FormControlDirective, FormLabelDirective, FormSelectDirective,
  ModalModule,
  ProgressBarComponent, ProgressComponent,
  RowComponent, SpinnerComponent, TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { Issue, IssueService, IssueStatus } from '../../core/services/issue.service';
import { ProwlarrCategory, ProwlarrService, ScoredSearchResult } from '../../core/services/prowlarr.service';
import { QBittorrentService, TorrentFile } from '../../core/services/qbittorrent.service';
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
    ModalModule,
    FormControlDirective, FormLabelDirective, FormSelectDirective, ReactiveFormsModule,
    DatePipe, DecimalPipe, SlicePipe
  ]
})
export class IssueComponent {
  private route              = inject(ActivatedRoute);
  private router             = inject(Router);
  private issueService       = inject(IssueService);
  private prowlarrService    = inject(ProwlarrService);
  private qbittorrentService = inject(QBittorrentService);
  private hub                = inject(HubService);
  private fb                 = inject(FormBuilder);
  readonly #destroyRef       = inject(DestroyRef);

  readonly issueId = this.route.snapshot.paramMap.get('issueId')!;

  readonly ISSUE_STATUSES: IssueStatus[] = ['MISSING', 'DOWNLOADING', 'DOWNLOADED'];

  issue         = signal<Issue | null>(null);
  loading       = signal(true);
  loadError     = signal<string | null>(null);
  searching     = signal(false);
  searchError   = signal<string | null>(null);
  searchResults = signal<ScoredSearchResult[]>([]);
  grabbingGuid  = signal<string | null>(null);
  grabSuccess   = signal<string | null>(null);
  currentPage   = signal(1);
  analyzing     = signal(false);
  analyzeError  = signal<string | null>(null);

  // --- État de la modale d'édition ---
  editModalVisible = signal(false);
  editSaving        = signal(false);
  editError         = signal<string | null>(null);

  editForm = this.fb.group({
    title:       [''],
    year:        [null as number | null],
    description: [''],
    status:      ['MISSING' as IssueStatus, Validators.required]
  });

  // --- État de la modale PACK ---
  packModalVisible    = signal(false);
  packModalStep       = signal<1 | 2>(1);
  pendingResult       = signal<ScoredSearchResult | null>(null);
  torrentFiles        = signal<TorrentFile[]>([]);
  pendingTorrentHash  = signal<string | null>(null);
  selectedFileIndices = signal<Set<number>>(new Set());
  applyingSelection   = signal(false);

  readonly pageSize     = 50;
  readonly totalPages   = computed(() => Math.ceil(this.searchResults().length / this.pageSize));
  readonly pageNumbers  = computed(() => Array.from({ length: this.totalPages() }, (_, i) => i + 1));
  readonly pagedResults = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize;
    return this.searchResults().slice(start, start + this.pageSize);
  });

  readonly allFilesSelected = computed(() =>
    this.torrentFiles().length > 0 &&
    this.torrentFiles().every(f => this.selectedFileIndices().has(f.index)));

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

  // --- Actions de la modale d'édition ---

  openEditModal(): void {
    const issue = this.issue();
    if (!issue) return;

    this.editForm.setValue({
      title:       issue.title ?? '',
      year:        issue.year,
      description: issue.description ?? '',
      status:      issue.status
    });
    this.editError.set(null);
    this.editModalVisible.set(true);
  }

  onEditModalVisibleChange(visible: boolean): void {
    if (!visible) this.closeEditModal();
  }

  closeEditModal(): void {
    this.editModalVisible.set(false);
    this.editError.set(null);
  }

  onSaveEdit(): void {
    if (this.editForm.invalid) return;

    const val = this.editForm.getRawValue();
    this.editSaving.set(true);
    this.editError.set(null);

    this.issueService.update(this.issueId, {
      title:       val.title?.trim() || null,
      year:        val.year,
      description: val.description?.trim() || null,
      status:      val.status!
    })
      .pipe(
        takeUntilDestroyed(this.#destroyRef),
        finalize(() => this.editSaving.set(false))
      )
      .subscribe({
        next: () => {
          this.issueService.getById(this.issueId)
            .pipe(takeUntilDestroyed(this.#destroyRef))
            .subscribe(issue => this.issue.set(issue));
          this.closeEditModal();
        },
        error: err => this.editError.set(err?.error?.message ?? 'Failed to save issue.')
      });
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

  onAnalyze(): void {
    this.analyzing.set(true);
    this.analyzeError.set(null);

    this.issueService.analyze(this.issueId)
      .pipe(
        takeUntilDestroyed(this.#destroyRef),
        finalize(() => this.analyzing.set(false))
      )
      .subscribe({
        next:  issue => this.issue.set(issue),
        error: err   => this.analyzeError.set(err?.error?.message ?? 'Analysis failed.')
      });
  }

  scoreBandColor(band: string | null): string {
    switch (band) {
      case 'Excellent': return 'success';
      case 'Bon':        return 'info';
      case 'Correct':    return 'warning';
      case 'Faible':
      case 'Illisible':  return 'danger';
      default:           return 'secondary';
    }
  }

  onGrab(result: ScoredSearchResult): void {
    if (this.grabbingGuid() !== null || this.packModalVisible()) return;

    const isTorrent = result.result.protocol?.toLowerCase() === 'torrent';
    if (isTorrent && result.result.torrentType === 'PACK') {
      this.pendingResult.set(result);
      this.packModalStep.set(1);
      this.packModalVisible.set(true);
      return;
    }

    this.executeGrab(result);
  }

  private executeGrab(result: ScoredSearchResult): void {
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

  // --- Actions de la modale PACK ---

  onPackModalVisibleChange(visible: boolean): void {
    if (!visible) this.closePackModal();
  }

  closePackModal(): void {
    this.packModalVisible.set(false);
    this.pendingResult.set(null);
    this.torrentFiles.set([]);
    this.selectedFileIndices.set(new Set());
    this.pendingTorrentHash.set(null);
    this.packModalStep.set(1);
  }

  onPackReplaceAll(): void {
    const result = this.pendingResult();
    if (!result) return;
    this.closePackModal();
    this.executeGrab(result);
  }

  onPackMissingOnly(): void {
    const result = this.pendingResult();
    if (!result?.result.downloadUrl) return;

    this.grabbingGuid.set(result.result.guid);
    this.grabSuccess.set(null);
    this.searchError.set(null);

    this.qbittorrentService.grab(result.result.downloadUrl, this.issueId, true)
      .pipe(
        takeUntilDestroyed(this.#destroyRef),
        finalize(() => this.grabbingGuid.set(null))
      )
      .subscribe({
        next: resp => {
          if (!resp.torrentHash || !resp.files?.length) {
            this.searchError.set('Could not retrieve file list from this torrent.');
            this.closePackModal();
            return;
          }
          this.pendingTorrentHash.set(resp.torrentHash);
          this.torrentFiles.set(resp.files);
          this.selectedFileIndices.set(new Set(resp.files.map(f => f.index)));
          this.packModalStep.set(2);
        },
        error: err => {
          this.searchError.set(err?.error?.message ?? 'Grab failed.');
          this.closePackModal();
        }
      });
  }

  onToggleFile(index: number): void {
    this.selectedFileIndices.update(set => {
      const next = new Set(set);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });
  }

  onToggleAllFiles(): void {
    if (this.allFilesSelected()) {
      this.selectedFileIndices.set(new Set());
    } else {
      this.selectedFileIndices.set(new Set(this.torrentFiles().map(f => f.index)));
    }
  }

  isFileSelected(index: number): boolean {
    return this.selectedFileIndices().has(index);
  }

  onApplySelection(): void {
    const hash = this.pendingTorrentHash();
    if (!hash) return;
    const selected = [...this.selectedFileIndices()];

    this.applyingSelection.set(true);

    this.qbittorrentService.applySelection(hash, this.issueId, selected)
      .pipe(
        takeUntilDestroyed(this.#destroyRef),
        finalize(() => { this.applyingSelection.set(false); this.closePackModal(); })
      )
      .subscribe({
        next:  () => this.grabSuccess.set(`Pack download started — ${selected.length} file(s) selected.`),
        error: err => this.searchError.set(err?.error?.message ?? 'Failed to apply selection.')
      });
  }

  // --- Utilitaires ---

  formatSize(bytes: number): string {
    if (bytes <= 0) return '—';
    const mb = bytes / 1_048_576;
    return mb >= 1000 ? `${(mb / 1024).toFixed(1)} GB` : `${mb.toFixed(0)} MB`;
  }

  formatBytes(bytes: number | null): string {
    if (bytes === null || bytes <= 0) return '—';
    if (bytes < 1024) return `${bytes.toFixed(0)} B`;
    if (bytes < 1_048_576) return `${(bytes / 1024).toFixed(1)} KB`;
    const mb = bytes / 1_048_576;
    return mb >= 1000 ? `${(mb / 1024).toFixed(2)} GB` : `${mb.toFixed(1)} MB`;
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
