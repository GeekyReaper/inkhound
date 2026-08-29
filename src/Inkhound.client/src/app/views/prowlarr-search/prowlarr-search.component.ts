import { Component, DestroyRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, finalize, interval, map, of, startWith, switchMap } from 'rxjs';
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
import { PackFetchStatus, QBittorrentService, TorrentFile } from '../../core/services/qbittorrent.service';
import { HubService } from '../../core/services/hub.service';
import { PageJobService } from '../../core/services/page-job.service';
import { JobPanelComponent } from '../job-panel/job-panel.component';
import { FileIssueMatcherComponent } from '../file-issue-matcher/file-issue-matcher.component';
import { formatSize } from '../../core/util/format-size';

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
    JobPanelComponent, FileIssueMatcherComponent
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
  // toutes ses issues — nécessaires pour l'assignation (auto ou manuelle) des fichiers d'un PACK.
  private resolvedVolumeId  = signal<string | null>(null);
  volumeIssues         = signal<Issue[]>([]);
  volumeIssuesLoaded   = signal(false);
  // Issues encore manquantes — seule cible de l'auto-appariement par numéro de tome, et base du
  // message "tout est déjà téléchargé".
  readonly missingIssues = computed(() => this.volumeIssues().filter(i => i.status === 'MISSING'));

  // --- État de la modale PACK ---
  packModalVisible    = signal(false);
  packModalStep       = signal<1 | 2>(1);
  pendingResult        = signal<SearchResultRow | null>(null);
  torrentFiles         = signal<TorrentFile[]>([]);
  pendingTorrentHash   = signal<string | null>(null);
  applyingSelection    = signal(false);

  // L'appariement fichiers ↔ issues est délégué au composant partagé ; on lit sa sélection courante.
  private readonly matcher = viewChild(FileIssueMatcherComponent);
  readonly selectedCount = computed(() => this.matcher()?.selection().length ?? 0);
  // Torrent ajouté puis mis en pause pendant la revue : statut live (polling), horodatage de départ
  // (délai de grâce avant l'alerte "stalled"), drapeau de validation (neutralise le nettoyage) et
  // drapeau d'annulation en cours.
  fetchStatus          = signal<PackFetchStatus | null>(null);
  fetchStartedAt       = signal(0);
  private pollTick      = signal(0); // incrémenté à chaque réponse de polling — force le recalcul des computed temporels
  selectionApplied     = signal(false);
  aborting             = signal(false);

  private readonly stalledStates = new Set(['stalleddl', 'metadl', 'error', 'missingfiles']);
  private static readonly METADATA_GRACE_MS = 15_000;

  // Temps écoulé depuis le début de la récupération (recalculé à chaque tour de polling via pollTick).
  private readonly fetchElapsedMs = computed(() => {
    this.pollTick();
    const started = this.fetchStartedAt();
    return started > 0 ? Date.now() - started : 0;
  });

  // Aucune source connue : QBittorrent l'annonce explicitement, ou (torrent en pause jamais annoncé)
  // l'indexeur rapporte 0 seeder.
  readonly noKnownSources = computed(() => {
    const s = this.fetchStatus();
    const indexerSeeders = this.pendingResult()?.result.seeders ?? null;
    if (s && s.numComplete === 0 && s.numSeeds <= 0) return true;
    if ((!s || s.numComplete < 0) && indexerSeeders === 0) return true;
    return false;
  });

  // Le torrent n'est pas exploitable pour l'instant — on prévient l'utilisateur (Continuer / Annuler).
  readonly isStalled = computed(() => {
    const s = this.fetchStatus();
    if (s && this.stalledStates.has(s.state.toLowerCase())) return true;
    if (this.torrentFiles().length === 0 && this.fetchElapsedMs() > ProwlarrSearchComponent.METADATA_GRACE_MS) return true;
    return this.noKnownSources();
  });

  // Suffixe optionnel du message "stalled" : " (status: stalledDL)".
  readonly stalledDetail = computed(() => {
    const state = this.fetchStatus()?.state;
    return state && state !== 'unknown' ? ` (status: ${state})` : '';
  });

  readonly pageSize     = 50;
  readonly totalPages   = computed(() => Math.ceil(this.searchResults().length / this.pageSize));
  readonly pageNumbers  = computed(() => Array.from({ length: this.totalPages() }, (_, i) => i + 1));
  readonly pagedResults = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize;
    return this.searchResults().slice(start, start + this.pageSize);
  });

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

    // Toutes les issues du volume résolu — utilisées pour l'auto-appariement (filtrées MISSING) et
    // l'assignation manuelle des fichiers d'un PACK, dans les deux modes.
    effect(() => {
      const volumeId = this.resolvedVolumeId();
      if (!volumeId) return;
      this.issueService.getByVolume(volumeId)
        .pipe(takeUntilDestroyed(this.#destroyRef))
        .subscribe(issues => {
          this.volumeIssues.set(issues);
          this.volumeIssuesLoaded.set(true);
        });
    });

    // Polling de l'état du torrent pendant la revue des fichiers d'un PACK (étape 2, avant
    // validation) : détecte une source indisponible et récupère la liste de fichiers si les
    // métadonnées n'étaient pas prêtes au moment du grab.
    effect((onCleanup) => {
      const hash = this.pendingTorrentHash();
      if (!hash || this.packModalStep() !== 2 || this.selectionApplied()) return;

      const sub = interval(2500)
        .pipe(
          startWith(0),
          switchMap(() => this.qbittorrentService.getPackFetchStatus(hash).pipe(catchError(() => of(null))))
        )
        .subscribe(status => {
          this.pollTick.update(t => t + 1);
          if (!status) return;
          this.fetchStatus.set(status);
          const noFilesYet = untracked(() => this.torrentFiles().length === 0);
          if (status.metadataReady && noFilesYet && status.files.length > 0) {
            this.torrentFiles.set(status.files); // le matcher (re)fait l'auto-appariement sur ce nouvel input
          }
        });
      onCleanup(() => sub.unsubscribe());
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
    if (!visible) this.onCancelPack();
  }

  closePackModal(): void {
    this.packModalVisible.set(false);
    this.pendingResult.set(null);
    this.torrentFiles.set([]); // démonte le matcher, qui se réinitialise au prochain jeu de fichiers
    this.pendingTorrentHash.set(null);
    this.packModalStep.set(1);
    this.fetchStatus.set(null);
    this.fetchStartedAt.set(0);
    this.selectionApplied.set(false);
    this.aborting.set(false);
  }

  // Annulation de la revue d'un PACK : tant que la sélection n'a pas été validée, on supprime le
  // torrent (et ses fichiers déjà téléchargés) de QBittorrent avant de fermer la modale.
  onCancelPack(): void {
    const hash = this.pendingTorrentHash();
    if (!hash || this.selectionApplied() || this.aborting()) {
      this.closePackModal();
      return;
    }
    this.aborting.set(true);
    this.qbittorrentService.abortSelectivePack(hash)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.closePackModal()))
      .subscribe({
        error: err => this.searchError.set(err?.error?.message ?? 'Failed to remove the torrent from QBittorrent.')
      });
  }

  // Mode issue uniquement (étape 1) : télécharge tout le torrent sans revue de fichiers.
  onPackReplaceAll(): void {
    const row = this.pendingResult();
    if (!row) return;
    this.closePackModal();
    this.executeGrab(row);
  }

  // Ajoute le torrent en pause et liste ses fichiers. Déclenché soit automatiquement (mode volume),
  // soit par le bouton "Select files individually" de l'étape 1 (mode issue). Si les métadonnées ne
  // sont pas encore prêtes (source lente/indisponible), on reste à l'étape 2 : le polling récupère
  // la liste plus tard et affiche l'avertissement "stalled" le cas échéant.
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
          if (!resp.torrentHash) {
            this.searchError.set('Could not add this torrent to QBittorrent.');
            this.closePackModal();
            return;
          }
          this.pendingTorrentHash.set(resp.torrentHash);
          this.fetchStartedAt.set(Date.now());
          this.packModalStep.set(2);

          if (resp.files?.length) {
            this.torrentFiles.set(resp.files); // le matcher fait l'auto-appariement
          }
        },
        error: err => {
          this.searchError.set(err?.error?.message ?? 'Grab failed.');
          this.closePackModal();
        }
      });
  }

  onApplySelection(): void {
    const hash = this.pendingTorrentHash();
    const row = this.pendingResult();
    const matcher = this.matcher();
    if (!hash || !row || !matcher) return;

    // La sélection du matcher est indexée sur la position dans torrentFiles() ; on retraduit vers
    // l'index de fichier QBittorrent attendu par apply-selection.
    const files = this.torrentFiles();
    const selectedFileIndices = matcher.selection().map(s => files[s.fileIndex].index);
    const overrides: Record<number, string> = {};
    for (const s of matcher.selection()) overrides[files[s.fileIndex].index] = s.issueId;

    this.applyingSelection.set(true);
    this.selectionApplied.set(true); // neutralise le nettoyage/polling pendant la validation
    const ids = this.grabIds();

    this.qbittorrentService.applySelection(
      hash, row.result.downloadUrl ?? '', row.result.title, row.result.indexer,
      ids.issueId, ids.volumeId, selectedFileIndices, overrides
    )
      .pipe(
        takeUntilDestroyed(this.#destroyRef),
        finalize(() => { this.applyingSelection.set(false); this.closePackModal(); })
      )
      .subscribe({
        next:  () => this.grabSuccess.set(`Pack download started — ${selectedFileIndices.length} file(s) selected.`),
        error: err => this.searchError.set(err?.error?.message ?? 'Failed to apply selection.')
      });
  }

  // --- Utilitaires ---

  protected readonly formatSize = formatSize;

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
