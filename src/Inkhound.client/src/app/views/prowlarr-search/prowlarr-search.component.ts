import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, map } from 'rxjs';
import { DatePipe, DecimalPipe } from '@angular/common';
import {
  AlertComponent, ButtonDirective,
  CardBodyComponent, CardComponent,
  ColComponent, ModalModule, RowComponent, SpinnerComponent, TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { Issue, IssueService } from '../../core/services/issue.service';
import {
  ProwlarrCategory, ProwlarrService,
  ProwlarrSearchResult, ScoredSearchResult, ScoredSearchResultVolumePack
} from '../../core/services/prowlarr.service';
import { QBittorrentService, TorrentFile } from '../../core/services/qbittorrent.service';
import { HubService } from '../../core/services/hub.service';
import { PageJobService } from '../../core/services/page-job.service';
import { JobPanelComponent } from '../job-panel/job-panel.component';

// Vue commune aux deux sources de résultats (recherche Issue ou recherche Volume) — coverage est
// uniquement renseigné en mode 'volume' (nombre d'issues manquantes couvertes par ce résultat).
export interface SearchResultRow {
  result: ProwlarrSearchResult;
  score: number;
  coverage: { covered: number; total: number } | null;
}

@Component({
  selector: 'app-prowlarr-search',
  standalone: true,
  templateUrl: './prowlarr-search.component.html',
  imports: [
    RowComponent, ColComponent, CardComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent, ButtonDirective, IconDirective,
    TableDirective, ModalModule,
    DatePipe, DecimalPipe,
    JobPanelComponent
  ]
})
export class ProwlarrSearchComponent {
  private issueService       = inject(IssueService);
  private prowlarrService    = inject(ProwlarrService);
  private qbittorrentService = inject(QBittorrentService);
  private hub                = inject(HubService);
  private pageJobs           = inject(PageJobService);
  private router             = inject(Router);
  readonly #destroyRef       = inject(DestroyRef);

  // mode==='issue' : targetId est un issueId. mode==='volume' : targetId est un volumeId.
  mode     = input.required<'issue' | 'volume'>();
  targetId = input.required<string>();

  // Clé distincte de router.url tout seul : sur la page Issue, IssueComponent utilise déjà
  // router.url comme clé PageJobService pour son propre job "Analyze". Comme les deux composants
  // cohabitent sur la même page, réutiliser la même clé ici ferait que la complétion du job de
  // recherche soit "consommée" (pageJobs.clear()) par l'effect de IssueComponent avant que ce
  // composant n'ait pu lire activeJobId() pour récupérer le résultat — le spinner de recherche
  // resterait alors bloqué indéfiniment.
  private readonly pageKey = `${this.router.url}#prowlarr-search`;

  searching     = signal(false);
  searchError   = signal<string | null>(null);
  searchResults = signal<SearchResultRow[]>([]);
  grabbingGuid  = signal<string | null>(null);
  grabSuccess   = signal<string | null>(null);
  currentPage   = signal(1);

  // Volume résolu (= targetId en mode 'volume', ou volume parent de l'issue en mode 'issue') et
  // ses issues manquantes — nécessaires pour l'assignation (auto ou manuelle) des fichiers d'un PACK.
  private resolvedVolumeId  = signal<string | null>(null);
  missingIssues        = signal<Issue[]>([]);
  missingIssuesLoaded   = signal(false);

  // --- État de la modale PACK ---
  packModalVisible    = signal(false);
  packModalStep       = signal<1 | 2>(1);
  pendingResult        = signal<SearchResultRow | null>(null);
  torrentFiles         = signal<TorrentFile[]>([]);
  pendingTorrentHash   = signal<string | null>(null);
  selectedFileIndices  = signal<Set<number>>(new Set());
  fileAssignments      = signal<Map<number, string>>(new Map()); // fileIndex -> issueId
  applyingSelection    = signal(false);

  readonly pageSize     = 50;
  readonly totalPages   = computed(() => Math.ceil(this.searchResults().length / this.pageSize));
  readonly pageNumbers  = computed(() => Array.from({ length: this.totalPages() }, (_, i) => i + 1));
  readonly pagedResults = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize;
    return this.searchResults().slice(start, start + this.pageSize);
  });

  // Fichiers assignés (auto ou manuellement) à une issue manquante — seuls ceux-là peuvent être
  // sélectionnés pour téléchargement.
  readonly assignableFiles = computed(() =>
    this.torrentFiles().filter(f => this.fileAssignments().has(f.index)));

  readonly allFilesSelected = computed(() =>
    this.assignableFiles().length > 0 &&
    this.assignableFiles().every(f => this.selectedFileIndices().has(f.index)));

  readonly activeJobId = this.pageJobs.activeJobId(this.pageKey);
  private handledJobIds = new Set<string>();

  private readonly currentJob = computed(() => {
    const jobId = this.activeJobId();
    return jobId ? this.hub.jobs().find(j => j.jobId === jobId) ?? null : null;
  });

  constructor() {
    // Résolution du volume cible : directement targetId en mode volume, sinon volumeId de l'issue.
    effect(() => {
      const mode = this.mode();
      const id = this.targetId();
      if (mode === 'volume') {
        this.resolvedVolumeId.set(id);
      } else {
        this.issueService.getById(id)
          .pipe(takeUntilDestroyed(this.#destroyRef))
          .subscribe(issue => this.resolvedVolumeId.set(issue.volumeId));
      }
    });

    // Issues manquantes du volume résolu — utilisées pour l'auto-appariement et l'assignation
    // manuelle des fichiers d'un PACK, dans les deux modes.
    effect(() => {
      const volumeId = this.resolvedVolumeId();
      if (!volumeId) return;
      this.issueService.getByVolume(volumeId)
        .pipe(takeUntilDestroyed(this.#destroyRef))
        .subscribe(issues => {
          this.missingIssues.set(issues.filter(i => i.status === 'MISSING'));
          this.missingIssuesLoaded.set(true);
        });
    });

    effect(() => {
      const job = this.currentJob();
      if (!job || this.handledJobIds.has(job.jobId)) return;
      if (job.state !== 'SUCCESS' && job.state !== 'ERROR') return;

      this.handledJobIds.add(job.jobId);
      this.pageJobs.clear(this.pageKey);

      if (job.state === 'ERROR') {
        this.searching.set(false);
        this.searchError.set('Search failed — see the console for details.');
        return;
      }

      const result$ = this.mode() === 'issue'
        ? this.prowlarrService.getSearchJobResult(job.jobId).pipe(
            map(results => results.map((r: ScoredSearchResult): SearchResultRow =>
              ({ result: r.result, score: r.score, coverage: null })))
          )
        : this.prowlarrService.getVolumeSearchJobResult(job.jobId).pipe(
            map(results => results.map((r: ScoredSearchResultVolumePack): SearchResultRow =>
              ({ result: r.result, score: r.score, coverage: { covered: r.coveredIssueCount, total: r.totalMissingIssueCount } })))
          );

      result$
        .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.searching.set(false)))
        .subscribe({
          next:  results => { this.searchResults.set(results); this.currentPage.set(1); },
          error: err     => this.searchError.set(err?.error?.message ?? 'Failed to load results.')
        });
    });
  }

  onSearch(): void {
    if (this.activeJobId()) return;

    this.searching.set(true);
    this.searchResults.set([]);
    this.searchError.set(null);
    this.grabSuccess.set(null);

    const start$ = this.mode() === 'issue'
      ? this.prowlarrService.startSearchJob(this.targetId())
      : this.prowlarrService.startVolumeSearchJob(this.targetId());

    start$
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  res => this.pageJobs.register(this.pageKey, res.jobId),
        error: err => { this.searchError.set(err?.error?.message ?? 'Search failed.'); this.searching.set(false); }
      });
  }

  private grabIds(): { issueId: string | null; volumeId: string | null } {
    return this.mode() === 'issue'
      ? { issueId: this.targetId(), volumeId: null }
      : { issueId: null, volumeId: this.targetId() };
  }

  onGrab(row: SearchResultRow): void {
    if (this.grabbingGuid() !== null || this.packModalVisible()) return;

    const isTorrent = row.result.protocol?.toLowerCase() === 'torrent';

    // Mode volume : toujours la revue de fichiers, y compris pour un résultat SINGLE (1 seul
    // fichier, pré-coché) — pas de raccourci "tout télécharger" comme en mode issue.
    if (this.mode() === 'volume') {
      if (!isTorrent) {
        this.searchError.set('Volume-level grab is only supported for torrent results.');
        return;
      }
      this.pendingResult.set(row);
      this.packModalVisible.set(true);
      this.packModalStep.set(2);
      this.startPackSelective(row);
      return;
    }

    if (isTorrent && row.result.torrentType === 'PACK') {
      this.pendingResult.set(row);
      this.packModalStep.set(1);
      this.packModalVisible.set(true);
      return;
    }

    this.executeGrab(row);
  }

  private executeGrab(row: SearchResultRow): void {
    this.grabbingGuid.set(row.result.guid);
    this.grabSuccess.set(null);
    this.searchError.set(null);

    const isTorrent = row.result.protocol?.toLowerCase() === 'torrent';

    if (isTorrent) {
      if (!row.result.downloadUrl) {
        this.searchError.set('No download URL available for this torrent.');
        this.grabbingGuid.set(null);
        return;
      }
      const ids = this.grabIds();
      this.qbittorrentService.grab(row.result.downloadUrl, row.result.title, row.result.indexer, ids.issueId, ids.volumeId)
        .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.grabbingGuid.set(null)))
        .subscribe({
          next:  () => this.grabSuccess.set(`"${row.result.title}" sent to QBittorrent.`),
          error: err => this.searchError.set(err?.error?.message ?? 'Grab failed.')
        });
    } else {
      // NZB/usenet : uniquement atteignable en mode issue (le mode volume bloque plus haut).
      this.prowlarrService.grab(row.result.guid, row.result.indexerId, this.targetId())
        .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.grabbingGuid.set(null)))
        .subscribe({
          next:  () => this.grabSuccess.set(`"${row.result.title}" sent to download client.`),
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
    this.fileAssignments.set(new Map());
    this.pendingTorrentHash.set(null);
    this.packModalStep.set(1);
  }

  // Mode issue uniquement (étape 1) : télécharge tout le torrent sans revue de fichiers.
  onPackReplaceAll(): void {
    const row = this.pendingResult();
    if (!row) return;
    this.closePackModal();
    this.executeGrab(row);
  }

  // Ajoute le torrent en pause, liste ses fichiers, et les auto-associe aux issues manquantes du
  // volume par numéro de tome détecté. Déclenché soit automatiquement (mode volume), soit par le
  // bouton "Select files individually" de l'étape 1 (mode issue).
  startPackSelective(row: SearchResultRow): void {
    if (!row.result.downloadUrl) {
      this.searchError.set('No download URL available for this torrent.');
      this.closePackModal();
      return;
    }

    this.grabbingGuid.set(row.result.guid);
    this.grabSuccess.set(null);
    this.searchError.set(null);

    const ids = this.grabIds();
    this.qbittorrentService.grab(row.result.downloadUrl, row.result.title, row.result.indexer, ids.issueId, ids.volumeId, true)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.grabbingGuid.set(null)))
      .subscribe({
        next: resp => {
          if (!resp.torrentHash || !resp.files?.length) {
            this.searchError.set('Could not retrieve file list from this torrent.');
            this.closePackModal();
            return;
          }
          this.pendingTorrentHash.set(resp.torrentHash);
          this.torrentFiles.set(resp.files);

          const assignments = new Map<number, string>();
          const takenIssueIds = new Set<string>();
          for (const file of resp.files) {
            if (file.detectedIssueNumber === null) continue;
            const issue = this.missingIssues().find(
              i => i.issueNumber === file.detectedIssueNumber && !takenIssueIds.has(i.id));
            if (issue) {
              assignments.set(file.index, issue.id);
              takenIssueIds.add(issue.id);
            }
          }
          this.fileAssignments.set(assignments);
          this.selectedFileIndices.set(new Set(assignments.keys()));
          this.packModalStep.set(2);
        },
        error: err => {
          this.searchError.set(err?.error?.message ?? 'Grab failed.');
          this.closePackModal();
        }
      });
  }

  onToggleFile(index: number): void {
    if (!this.fileAssignments().has(index)) return;
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
      this.selectedFileIndices.set(new Set(this.assignableFiles().map(f => f.index)));
    }
  }

  isFileSelected(index: number): boolean {
    return this.selectedFileIndices().has(index);
  }

  isFileAssignable(index: number): boolean {
    return this.fileAssignments().has(index);
  }

  assignedIssueNumber(index: number): number | null {
    const issueId = this.fileAssignments().get(index);
    if (!issueId) return null;
    return this.missingIssues().find(i => i.id === issueId)?.issueNumber ?? null;
  }

  // Issues manquantes disponibles pour assignation manuelle d'un fichier — exclut celles déjà
  // assignées à un AUTRE fichier de la sélection courante.
  availableIssuesFor(index: number): Issue[] {
    const takenElsewhere = new Set(
      [...this.fileAssignments().entries()].filter(([i]) => i !== index).map(([, id]) => id));
    return this.missingIssues().filter(i => !takenElsewhere.has(i.id));
  }

  onManualAssign(index: number, issueId: string): void {
    if (!issueId) {
      this.fileAssignments.update(m => { const next = new Map(m); next.delete(index); return next; });
      this.selectedFileIndices.update(set => { const next = new Set(set); next.delete(index); return next; });
      return;
    }
    this.fileAssignments.update(m => { const next = new Map(m); next.set(index, issueId); return next; });
    this.selectedFileIndices.update(set => new Set(set).add(index));
  }

  onApplySelection(): void {
    const hash = this.pendingTorrentHash();
    const row = this.pendingResult();
    if (!hash || !row) return;
    const selected = [...this.selectedFileIndices()];
    const overrides: Record<number, string> = {};
    for (const index of selected) {
      const issueId = this.fileAssignments().get(index);
      if (issueId) overrides[index] = issueId;
    }

    this.applyingSelection.set(true);
    const ids = this.grabIds();

    this.qbittorrentService.applySelection(
      hash, row.result.downloadUrl ?? '', row.result.title, row.result.indexer,
      ids.issueId, ids.volumeId, selected, overrides
    )
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
}
