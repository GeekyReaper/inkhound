import { Component, computed, DestroyRef, effect, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { filter, finalize, switchMap } from 'rxjs';
import {
  AlertComponent,
  ButtonCloseDirective,
  ButtonDirective,
  BadgeComponent,
  CardBodyComponent,
  CardComponent,
  ColComponent,
  ContainerComponent,
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  ProgressBarComponent,
  ProgressComponent,
  RowComponent,
  SpinnerComponent,
  TooltipDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { AGE_RATINGS, AgeRating, AgeRatingOption, ImportScanFile, RefreshVolumeOptions, Volume, VolumeService, VolumeStatus } from '../../core/services/volume.service';
import { Issue, IssueCategory, IssueService } from '../../core/services/issue.service';
import { DownloadStatus, QBittorrentService } from '../../core/services/qbittorrent.service';
import { SelectPathComponent } from '../select-path/select-path.component';
import { ProwlarrSearchComponent } from '../prowlarr-search/prowlarr-search.component';
import { FileIssueMatcherComponent } from '../file-issue-matcher/file-issue-matcher.component';
import { IssueCardComponent } from './issue-card/issue-card.component';
import { HubService } from '../../core/services/hub.service';
import { UpdatedData } from '../../core/models/hub.models';
import { PageJobService } from '../../core/services/page-job.service';
import { JobPanelComponent } from '../job-panel/job-panel.component';
import { Library, LibraryService } from '../../core/services/library.service';
import { SmartDatePipe } from '../../core/pipes/smart-date.pipe';

@Component({
  selector: 'app-volume',
  templateUrl: './volume.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    BadgeComponent,
    SpinnerComponent, AlertComponent, ButtonDirective, IconDirective,
    SelectPathComponent, ProwlarrSearchComponent, FileIssueMatcherComponent,
    ModalComponent, ModalHeaderComponent, ModalBodyComponent,
    ModalFooterComponent, ModalTitleDirective, ButtonCloseDirective,
    FormCheckComponent, FormCheckInputDirective, FormCheckLabelDirective,
    ProgressComponent, ProgressBarComponent, TooltipDirective,
    JobPanelComponent, IssueCardComponent, SmartDatePipe
  ]
})
export class VolumeComponent {
  // Ordre d'affichage fixe des sous-sections du bloc "Extra" (les catégories absentes de la série
  // sont simplement omises) + libellés — pas d'i18n dans ce projet, texte en dur comme le reste.
  private static readonly EXTRA_CATEGORY_ORDER: IssueCategory[] = ['Special', 'SpecialEdition', 'Omnibus', 'BestOf', 'Roman'];
  private static readonly CATEGORY_LABELS: Partial<Record<IssueCategory, string>> = {
    Special: 'Special',
    SpecialEdition: 'Special Edition',
    Omnibus: 'Omnibus',
    BestOf: 'Best Of',
    Roman: 'Novel'
  };

  private route         = inject(ActivatedRoute);
  private router        = inject(Router);
  private volumeService = inject(VolumeService);
  private issueService  = inject(IssueService);
  private libraryService = inject(LibraryService);
  private qbService     = inject(QBittorrentService);
  private hub           = inject(HubService);
  private pageJobs      = inject(PageJobService);
  readonly #destroyRef  = inject(DestroyRef);

  volume        = signal<Volume | null>(null);
  library       = signal<Library | null>(null);

  isManual      = computed(() => (this.volume()?.sourceType ?? '').toLowerCase() === 'manual');
  isRefreshable = computed(() => !this.isManual());

  // Pourcentage d'issues téléchargées — même calcul que library.component.ts (progressPercent),
  // affiché ici pour rappeler l'avancement du volume sans redescendre à la vue library.
  progressPercent = computed(() => {
    const vol = this.volume();
    if (!vol || !vol.countOfIssues) return 0;
    return Math.round((vol.countOfDownloadedIssues / vol.countOfIssues) * 100);
  });

  // Disponibilité des étapes de la popup Refresh — grisées si non applicables (pas de fichier à
  // resynchroniser, ou library non rattachée à Kavita).
  //
  // Calculé sur les issues chargées, toutes catégories confondues, et NON sur
  // volume.countOfDownloadedIssues : ce compteur ne retient que les issues `Standard` (il mesure la
  // complétion de la série). Un volume dont seuls des hors-séries, intégrales ou omnibus sont
  // téléchargés affichait donc 0 et grisait à tort "Check files" et "Regenerate ComicInfo.xml".
  hasDownloadedIssues = computed(() => this.issues().some(i => i.status === 'DOWNLOADED'));
  hasKavitaLibrary = computed(() => {
    const lib = this.library();
    return !!lib && (lib.kavitaLibraryId > 0 || !!lib.kavitaPath);
  });

  refreshModalVisible = signal(false);
  refreshOptions = signal<RefreshVolumeOptions>({
    syncFromSource: true, syncNewIssuesOnly: true, checkFiles: true,
    recalculateStatistics: true, regenerateComicInfo: true, regenerateComicInfoNewOnly: true,
    scanKavita: true
  });
  refreshCanRun = computed(() => {
    const o = this.refreshOptions();
    return o.syncFromSource || o.checkFiles || o.recalculateStatistics || o.regenerateComicInfo || o.scanKavita;
  });

  sourceLabel = computed(() => {
    const type = (this.volume()?.sourceType ?? '').toLowerCase();
    if (type === 'manual')     return 'Manual';
    if (type === 'bedetheque') return 'Bedetheque';
    if (type === 'comicvine')  return 'ComicVine';
    return type || 'Unknown';
  });
  sourceColor = computed(() => {
    const type = (this.volume()?.sourceType ?? '').toLowerCase();
    if (type === 'manual')     return 'warning';
    if (type === 'bedetheque') return 'success';
    return 'info'; // comicvine + fallback
  });

  // Suivi du Job Rematch/Refresh en cours (déclenché depuis cette page via "Refresh", ou depuis
  // la page "match" via "Rematch" — enregistré sous la clé de CETTE page dans les deux cas, cf.
  // volume-match.component.ts) — même pattern que issue.component.ts pour l'analyse CBZ.
  private readonly pageKey = this.router.url;
  private handledJobIds = new Set<string>();
  readonly activeJobId = this.pageJobs.activeJobId(this.pageKey);
  private readonly currentJob = computed(() => {
    const id = this.activeJobId();
    return id ? this.hub.jobs().find(j => j.jobId === id) ?? null : null;
  });

  loading       = signal(true);
  error         = signal<string | null>(null);
  issues        = signal<Issue[]>([]);
  issuesLoading = signal(false);

  // Bloc "Issues" : uniquement les tomes Standard. Bloc "Extra" : le reste, groupé par catégorie
  // dans un ordre fixe, catégories absentes omises — voir EXTRA_CATEGORY_ORDER.
  // Statut du téléchargement en cours par issue (vide si aucune issue n'est DOWNLOADING) —
  // distingue une issue qui télécharge d'une issue bloquée, voir IssueCardComponent.
  downloadStatusByIssue = signal<Map<string, DownloadStatus>>(new Map());

  standardIssues = computed(() => this.issues().filter(i => i.category === 'Standard'));
  extraGroups = computed(() => {
    const extra = this.issues().filter(i => i.category !== 'Standard');
    return VolumeComponent.EXTRA_CATEGORY_ORDER
      .map(category => ({
        category,
        label: VolumeComponent.CATEGORY_LABELS[category]!,
        issues: extra.filter(i => i.category === category).sort((a, b) => a.issueNumber - b.issueNumber)
      }))
      .filter(group => group.issues.length > 0);
  });
  extraIssuesCount = computed(() => this.extraGroups().reduce((n, group) => n + group.issues.length, 0));

  // --- Import d'un dossier avec revue fichiers ↔ issues ---
  importVisible       = signal(false);          // pioche du dossier (app-select-path)
  importDirectory     = signal<string | null>(null);
  importReviewVisible = signal(false);          // modale de revue
  importScanFiles     = signal<ImportScanFile[]>([]);
  importScanLoading   = signal(false);
  importScanError     = signal<string | null>(null);
  importApplying      = signal(false);
  private readonly importMatcher = viewChild(FileIssueMatcherComponent);
  readonly importSelectedCount = computed(() => this.importMatcher()?.selection().length ?? 0);

  confirmDeleteVisible = signal(false);
  deleting             = signal(false);
  deleteError          = signal<string | null>(null);
  deleteFilesToo       = signal(false);
  deleteWarning        = signal<string | null>(null);

  readonly ageRatings: AgeRatingOption[] = AGE_RATINGS;
  savingRating      = signal(false);

  // Bascule MONITORED ⇄ PAUSED — proposée uniquement sur un volume incomplet (un volume COMPLETED
  // n'a aucune recherche à suspendre ; le backend refuse d'ailleurs en 409).
  savingStatus      = signal(false);
  statusError       = signal<string | null>(null);
  canToggleStatus   = computed(() => this.volume()?.status !== 'COMPLETED');
  isPaused          = computed(() => this.volume()?.status === 'PAUSED');

  constructor() {
    this.route.parent!.params
      .pipe(
        switchMap(params => this.volumeService.getById(params['volumeId'])),
        takeUntilDestroyed(this.#destroyRef)
      )
      .subscribe({
        next: volume => {
          this.volume.set(volume);
          this.loading.set(false);
          this.loadIssues(volume.id);
          this.libraryService.getById(volume.libraryId)
            .pipe(takeUntilDestroyed(this.#destroyRef))
            .subscribe({ next: lib => this.library.set(lib) });
        },
        error: err => {
          this.error.set(err?.error?.message ?? 'Volume not found.');
          this.loading.set(false);
        }
      });

    const dataUpdated$ = toObservable(this.hub.lastDataUpdated).pipe(
      filter((d): d is UpdatedData => d !== null),
      takeUntilDestroyed(this.#destroyRef)
    );

    dataUpdated$.pipe(
      filter(d => d.dataType.endsWith('Volume') && d.id === this.volume()?.id)
    ).subscribe(() =>
      this.volumeService.getById(this.volume()!.id)
        .pipe(takeUntilDestroyed(this.#destroyRef))
        .subscribe(vol => this.volume.set(vol))
    );

    dataUpdated$.pipe(
      filter(d => d.dataType.endsWith('Issue'))
    ).subscribe(() => {
      const vol = this.volume();
      if (vol) this.loadIssues(vol.id);
    });

    effect(() => {
      const job = this.currentJob();
      if (!job || this.handledJobIds.has(job.jobId)) return;
      if (job.state !== 'SUCCESS' && job.state !== 'ERROR') return;
      this.handledJobIds.add(job.jobId);
      this.pageJobs.clear(this.pageKey);
      if (job.state === 'ERROR') {
        this.error.set('Job failed — see the job console for details.');
        return;
      }
      // Job terminé (Refresh / Rematch / Import) : on recharge explicitement volume + issues. Le
      // rematch ne diffuse un ManagerDataUpdated que pour le Volume (via RecalculateVolumeStatistics),
      // jamais par issue — sans ce rechargement, les changements de catégorie/numéro des issues ne
      // sont visibles qu'après un refresh manuel de la page.
      const vol = this.volume();
      if (vol) {
        this.volumeService.getById(vol.id)
          .pipe(takeUntilDestroyed(this.#destroyRef))
          .subscribe(v => this.volume.set(v));
        this.loadIssues(vol.id);
      }
    });
  }

  private loadIssues(volumeId: string): void {
    this.issuesLoading.set(true);
    this.issueService.getByVolume(volumeId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  issues => {
          this.issues.set(issues.sort((a, b) => a.issueNumber - b.issueNumber));
          this.issuesLoading.set(false);
          this.loadDownloadStatuses(volumeId, issues);
        },
        error: ()     => { this.issuesLoading.set(false); }
      });
  }

  // Statut de téléchargement par issue — un seul appel pour tout le volume, et seulement s'il y a
  // quelque chose en cours (chaque appel interroge qBittorrent en direct).
  private loadDownloadStatuses(volumeId: string, issues: Issue[]): void {
    if (!issues.some(i => i.status === 'DOWNLOADING')) {
      this.downloadStatusByIssue.set(new Map());
      return;
    }

    this.qbService.getVolumeDownloads(volumeId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: items => {
          // Une issue peut avoir plusieurs lignes (PACK relancé) : la plus récente fait foi, et
          // l'endpoint les renvoie déjà triées par date décroissante.
          const map = new Map<string, DownloadStatus>();
          for (const item of items) {
            if (!map.has(item.issueId)) map.set(item.issueId, item.status);
          }
          this.downloadStatusByIssue.set(map);
        },
        error: () => { /* indicateur secondaire : le badge reste celui de l'issue */ }
      });
  }

  downloadStatusFor(issueId: string): DownloadStatus | null {
    return this.downloadStatusByIssue().get(issueId) ?? null;
  }

  goBack(): void {
    const libraryId = this.volume()?.libraryId;
    if (libraryId) this.router.navigate(['/library', libraryId]);
  }

  goEdit(): void {
    this.router.navigate(['edit'], { relativeTo: this.route });
  }

  goMatch(): void {
    this.router.navigate(['match'], { relativeTo: this.route });
  }

  // Ouvre la popup "Refresh" — cases pré-cochées, grisées si non applicables (pas d'issue
  // téléchargée pour ComicInfo, library non rattachée à Kavita pour le scan).
  openRefreshModal(): void {
    if (this.activeJobId()) return;
    this.refreshOptions.set({
      syncFromSource: true,
      syncNewIssuesOnly: true,
      checkFiles: this.hasDownloadedIssues(),
      recalculateStatistics: true,
      regenerateComicInfo: this.hasDownloadedIssues(),
      regenerateComicInfoNewOnly: true,
      scanKavita: this.hasKavitaLibrary()
    });
    this.refreshModalVisible.set(true);
  }

  toggleRefreshOption(key: keyof RefreshVolumeOptions): void {
    this.refreshOptions.update(o => ({ ...o, [key]: !o[key] }));
  }

  // Radio "NEW issues only" / "ALL issues" sous la case "Sync with source" (ne se toggle pas).
  setSyncScope(newIssuesOnly: boolean): void {
    this.refreshOptions.update(o => ({ ...o, syncNewIssuesOnly: newIssuesOnly }));
  }

  // Radio "NEW only" / "ALL" sous la case "Regenerate ComicInfo.xml" (ne se toggle pas).
  setComicInfoScope(newOnly: boolean): void {
    this.refreshOptions.update(o => ({ ...o, regenerateComicInfoNewOnly: newOnly }));
  }

  confirmRefresh(): void {
    const vol = this.volume();
    if (!vol) return;
    this.refreshModalVisible.set(false);
    this.volumeService.refresh(vol.id, this.refreshOptions())
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  res => this.pageJobs.register(this.pageKey, res.jobId),
        error: err => this.error.set(err?.error?.message ?? 'Refresh failed.')
      });
  }

  goIssue(issue: Issue): void {
    this.router.navigate(['issue', issue.id], { relativeTo: this.route.parent });
  }

  // Dossier choisi → ouvre la modale de revue et lance le scan.
  onImportSelected(path: string): void {
    if (!path || !this.volume()) return;
    this.importDirectory.set(path);
    this.importScanFiles.set([]);
    this.importScanError.set(null);
    this.error.set(null);
    this.importReviewVisible.set(true);
    this.scanImport();
  }

  private scanImport(): void {
    const vol = this.volume();
    const dir = this.importDirectory();
    if (!vol || !dir) return;
    this.importScanLoading.set(true);
    this.volumeService.scanImportDirectory(vol.id, dir)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.importScanLoading.set(false)))
      .subscribe({
        next:  res => this.importScanFiles.set(res.files),
        error: err => this.importScanError.set(err?.error?.message ?? 'Failed to scan the directory.')
      });
  }

  onImportReviewVisibleChange(visible: boolean): void {
    if (!visible) this.closeImportReview();
  }

  closeImportReview(): void {
    this.importReviewVisible.set(false);
    this.importDirectory.set(null);
    this.importScanFiles.set([]);
    this.importScanError.set(null);
  }

  confirmImport(): void {
    const vol = this.volume();
    const dir = this.importDirectory();
    const matcher = this.importMatcher();
    if (!vol || !dir || !matcher || this.importApplying()) return;

    const files = this.importScanFiles();
    const fileIssueMap: Record<string, string> = {};
    for (const s of matcher.selection()) fileIssueMap[files[s.fileIndex].name] = s.issueId;

    this.importApplying.set(true);
    this.error.set(null);
    this.volumeService.importFromDirectory(vol.id, dir, fileIssueMap)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.importApplying.set(false)))
      .subscribe({
        next:  res => { this.pageJobs.register(this.pageKey, res.jobId); this.closeImportReview(); },
        error: err => this.error.set(err?.error?.message ?? 'Import failed.')
      });
  }

  requestDelete(): void {
    this.deleteError.set(null);
    this.deleteWarning.set(null);
    // La case repart toujours décochée : l'effacement des fichiers doit être un choix explicite.
    this.deleteFilesToo.set(false);
    this.confirmDeleteVisible.set(true);
  }

  confirmDelete(): void {
    const vol = this.volume();
    if (!vol) return;

    this.deleting.set(true);
    this.deleteError.set(null);

    this.volumeService.delete(vol.id, this.deleteFilesToo())
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: res => {
          this.deleting.set(false);
          // Volume supprimé mais répertoire intact : on garde la modale ouverte pour que
          // l'utilisateur voie l'avertissement avant de quitter la page.
          if (res?.fileWarning) {
            this.deleteWarning.set(res.fileWarning);
            return;
          }
          this.confirmDeleteVisible.set(false);
          this.router.navigate(['/library', vol.libraryId]);
        },
        error: err => {
          this.deleteError.set(err?.error?.message ?? 'Failed to delete volume.');
          this.deleting.set(false);
        }
      });
  }

  // Fermeture de la modale, quel que soit le moyen (Cancel, croix, Escape) : si le volume a déjà
  // été supprimé et qu'un avertissement est affiché, la page n'existe plus → retour à la librairie.
  closeDeleteModal(): void {
    const vol = this.volume();
    const wasDeleted = !!this.deleteWarning();
    this.confirmDeleteVisible.set(false);
    if (wasDeleted && vol) this.router.navigate(['/library', vol.libraryId]);
  }

  onAgeRatingChange(value: string): void {
    const vol = this.volume();
    if (!vol || this.savingRating()) return;
    this.savingRating.set(true);
    this.volumeService.patchAgeRating(vol.id, value as AgeRating)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({ complete: () => this.savingRating.set(false) });
  }

  toggleStatus(): void {
    const vol = this.volume();
    if (!vol || this.savingStatus() || !this.canToggleStatus()) return;
    const next = this.isPaused() ? 'MONITORED' : 'PAUSED';
    this.savingStatus.set(true);
    this.volumeService.patchStatus(vol.id, next)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.savingStatus.set(false)))
      .subscribe({
        next: updated => { this.volume.set(updated); this.statusError.set(null); },
        error: err => this.statusError.set(err?.error?.message ?? 'Failed to update the volume status.')
      });
  }

  volumeStatusBadgeClass(status: VolumeStatus): string {
    const map: Record<VolumeStatus, string> = {
      MONITORED: 'badge bg-primary',
      COMPLETED: 'badge bg-success',
      PAUSED:    'badge bg-secondary'
    };
    return map[status];
  }
}
