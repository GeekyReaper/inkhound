import { Component, computed, DestroyRef, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { filter, switchMap } from 'rxjs';
import {
  AlertComponent,
  ButtonCloseDirective,
  ButtonDirective,
  BadgeComponent,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
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
  RowComponent,
  SpinnerComponent
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { AGE_RATINGS, AgeRating, AgeRatingOption, RefreshVolumeOptions, Volume, VolumeService, VolumeStatus } from '../../core/services/volume.service';
import { Issue, IssueService, IssueStatus } from '../../core/services/issue.service';
import { SelectPathComponent } from '../select-path/select-path.component';
import { ProwlarrSearchComponent } from '../prowlarr-search/prowlarr-search.component';
import { HubService } from '../../core/services/hub.service';
import { UpdatedData } from '../../core/models/hub.models';
import { PageJobService } from '../../core/services/page-job.service';
import { JobPanelComponent } from '../job-panel/job-panel.component';
import { Library, LibraryService } from '../../core/services/library.service';

@Component({
  selector: 'app-volume',
  templateUrl: './volume.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent, CardFooterComponent,
    BadgeComponent,
    SpinnerComponent, AlertComponent, ButtonDirective, IconDirective,
    SelectPathComponent, ProwlarrSearchComponent,
    ModalComponent, ModalHeaderComponent, ModalBodyComponent,
    ModalFooterComponent, ModalTitleDirective, ButtonCloseDirective,
    FormCheckComponent, FormCheckInputDirective, FormCheckLabelDirective,
    JobPanelComponent
  ]
})
export class VolumeComponent {
  private route         = inject(ActivatedRoute);
  private router        = inject(Router);
  private volumeService = inject(VolumeService);
  private issueService  = inject(IssueService);
  private libraryService = inject(LibraryService);
  private hub           = inject(HubService);
  private pageJobs      = inject(PageJobService);
  readonly #destroyRef  = inject(DestroyRef);

  volume        = signal<Volume | null>(null);
  library       = signal<Library | null>(null);

  isManual      = computed(() => (this.volume()?.sourceType ?? '').toLowerCase() === 'manual');
  isRefreshable = computed(() => !this.isManual());

  // Disponibilité des étapes de la popup Refresh — grisées si non applicables (pas de fichier à
  // resynchroniser, ou library non rattachée à Kavita).
  hasDownloadedIssues = computed(() => (this.volume()?.countOfDownloadedIssues ?? 0) > 0);
  hasKavitaLibrary = computed(() => {
    const lib = this.library();
    return !!lib && (lib.kavitaLibraryId > 0 || !!lib.kavitaPath);
  });

  refreshModalVisible = signal(false);
  refreshOptions = signal<RefreshVolumeOptions>({
    syncFromSource: true, recalculateStatistics: true, regenerateComicInfo: true, scanKavita: true
  });
  refreshCanRun = computed(() => {
    const o = this.refreshOptions();
    return o.syncFromSource || o.recalculateStatistics || o.regenerateComicInfo || o.scanKavita;
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
  importVisible = signal(false);
  importing     = signal(false);
  importSuccess = signal(false);

  confirmDeleteVisible = signal(false);
  deleting             = signal(false);
  deleteError          = signal<string | null>(null);

  readonly ageRatings: AgeRatingOption[] = AGE_RATINGS;
  savingRating      = signal(false);

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
      if (job.state === 'ERROR') this.error.set('Rematch/Refresh failed — see the job console for details.');
      // Les données à jour (Volume + Issues) arrivent via lastDataUpdated, déjà écouté ci-dessus.
    });
  }

  private loadIssues(volumeId: string): void {
    this.issuesLoading.set(true);
    this.issueService.getByVolume(volumeId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  issues => { this.issues.set(issues.sort((a, b) => a.issueNumber - b.issueNumber)); this.issuesLoading.set(false); },
        error: ()     => { this.issuesLoading.set(false); }
      });
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
      recalculateStatistics: true,
      regenerateComicInfo: this.hasDownloadedIssues(),
      scanKavita: this.hasKavitaLibrary()
    });
    this.refreshModalVisible.set(true);
  }

  toggleRefreshOption(key: keyof RefreshVolumeOptions): void {
    this.refreshOptions.update(o => ({ ...o, [key]: !o[key] }));
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

  onImportSelected(path: string): void {
    console.log('Selected path for import:', path);
    if (!path) return;
    const volumeId = this.volume()?.id;
    if (!volumeId) return;

    console.log('Starting import for volume ID:', volumeId);

    this.importing.set(true);
    this.importSuccess.set(false);
    this.error.set(null);

    this.volumeService.importFromDirectory(volumeId, path)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  () => { this.importing.set(false); this.importSuccess.set(true); },
        error: err => { this.error.set(err?.error?.message ?? 'Import failed.'); this.importing.set(false); }
      });
  }

  requestDelete(): void {
    this.deleteError.set(null);
    this.confirmDeleteVisible.set(true);
  }

  confirmDelete(): void {
    const vol = this.volume();
    if (!vol) return;

    this.deleting.set(true);
    this.deleteError.set(null);

    this.volumeService.delete(vol.id)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: () => {
          this.deleting.set(false);
          this.confirmDeleteVisible.set(false);
          this.router.navigate(['/library', vol.libraryId]);
        },
        error: err => {
          this.deleteError.set(err?.error?.message ?? 'Failed to delete volume.');
          this.deleting.set(false);
        }
      });
  }

  onAgeRatingChange(value: string): void {
    const vol = this.volume();
    if (!vol || this.savingRating()) return;
    this.savingRating.set(true);
    this.volumeService.patchAgeRating(vol.id, value as AgeRating)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({ complete: () => this.savingRating.set(false) });
  }

  volumeStatusBadgeClass(status: VolumeStatus): string {
    const map: Record<VolumeStatus, string> = {
      MONITORED: 'badge bg-primary',
      COMPLETED: 'badge bg-success',
      PAUSED:    'badge bg-secondary'
    };
    return map[status];
  }

  issueStatusBadgeClass(status: IssueStatus): string {
    const map: Record<IssueStatus, string> = {
      DOWNLOADING: 'badge bg-info text-dark',
      DOWNLOADED:  'badge bg-success',
      MISSING:     'badge bg-danger'
    };
    return map[status];
  }
}
