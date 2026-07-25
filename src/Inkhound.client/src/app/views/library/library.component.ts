import { Component, computed, DestroyRef, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { filter, switchMap } from 'rxjs';
import {
  AlertComponent,
  BadgeComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  ColComponent,
  ContainerComponent,
  ProgressBarComponent,
  ProgressComponent,
  RowComponent,
  SpinnerComponent,
  TooltipDirective
} from '@coreui/angular';
import { Library, LibraryService, libraryPageKey } from '../../core/services/library.service';
import { KavitaService } from '../../core/services/kavita.service';
import { AGE_RATINGS, AgeRating, Volume, VolumeService, VolumeStatus } from '../../core/services/volume.service';
import { HubService } from '../../core/services/hub.service';
import { PageJobService } from '../../core/services/page-job.service';
import { JobPanelComponent } from '../job-panel/job-panel.component';
import { UpdatedData } from '../../core/models/hub.models';

@Component({
  selector: 'app-library',
  templateUrl: './library.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent, ButtonDirective, DatePipe, RouterLink,
    BadgeComponent, ProgressComponent, ProgressBarComponent, TooltipDirective,
    JobPanelComponent
  ]
})
export class LibraryComponent {
  private route          = inject(ActivatedRoute);
  private libraryService = inject(LibraryService);
  private kavitaService  = inject(KavitaService);
  private volumeService  = inject(VolumeService);
  private hub            = inject(HubService);
  private pageJobs       = inject(PageJobService);
  readonly #destroyRef   = inject(DestroyRef);

  library        = signal<Library | null>(null);
  loading        = signal(true);
  error          = signal<string | null>(null);
  syncing        = signal(false);
  syncDone       = signal<string | null>(null);
  volumes        = signal<Volume[]>([]);
  volumesLoading = signal(false);

  // Clé de page dérivée (pas figée à la construction) : ce composant peut être réutilisé d'une
  // library à l'autre sans être détruit — d'où le switchMap sur route.params ci-dessous.
  private readonly pageKey = computed(() => {
    const lib = this.library();
    return lib ? libraryPageKey(lib.id) : null;
  });

  // Job actif associé à cette page (ex: peuplement des issues d'un Volume ajouté depuis
  // VolumeAddComponent, cf. PageJobService) — un seul à la fois, tant qu'il tourne le bouton
  // "+ Add" reste désactivé.
  readonly activeJobId = computed(() => {
    const key = this.pageKey();
    return key ? this.pageJobs.activeJobId(key)() : null;
  });

  private readonly currentJob = computed(() => {
    const jobId = this.activeJobId();
    return jobId ? this.hub.jobs().find(j => j.jobId === jobId) ?? null : null;
  });

  private handledJobIds = new Set<string>();

  constructor() {
    this.kavitaService.loadLibraries();

    effect(() => {
      const job = this.currentJob();
      if (!job || this.handledJobIds.has(job.jobId)) return;
      if (job.state !== 'SUCCESS' && job.state !== 'ERROR') return;

      this.handledJobIds.add(job.jobId);
      const key = this.pageKey();
      if (key) this.pageJobs.clear(key);
      if (job.state === 'ERROR') this.error.set('Failed to add volume — see the console for details.');
      // La liste des volumes est déjà rafraîchie par l'abonnement lastDataUpdated ci-dessous.
    });

    this.route.params
      .pipe(
        switchMap(params => this.libraryService.getById(params['id'])),
        takeUntilDestroyed(this.#destroyRef)
      )
      .subscribe({
        next: lib => {
          this.library.set(lib);
          this.loading.set(false);
          this.loadVolumes(lib.id);
        },
        error: err => {
          this.error.set(err?.error?.message ?? 'Library not found.');
          this.loading.set(false);
        }
      });

    const dataUpdated$ = toObservable(this.hub.lastDataUpdated).pipe(
      filter((d): d is UpdatedData => d !== null),
      takeUntilDestroyed(this.#destroyRef)
    );

    dataUpdated$.pipe(
      filter(d => d.dataType.endsWith('Library') && d.id === this.library()?.id)
    ).subscribe(() =>
      this.libraryService.getById(this.library()!.id)
        .pipe(takeUntilDestroyed(this.#destroyRef))
        .subscribe(lib => this.library.set(lib))
    );

    dataUpdated$.pipe(
      filter(d => d.dataType.endsWith('Volume'))
    ).subscribe(() => {
      const lib = this.library();
      if (lib) this.loadVolumes(lib.id);
    });
  }

  private loadVolumes(libraryId: string): void {
    this.volumesLoading.set(true);
    this.volumeService.getByLibrary(libraryId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  volumes => { this.volumes.set(volumes); this.volumesLoading.set(false); },
        error: ()      => { this.volumesLoading.set(false); }
      });
  }

  getKavitaLibraryName(id: number): string {
    if (id === 0) return 'None';
    return this.kavitaService.libraries().find(l => l.id === id)?.name ?? `#${id}`;
  }

  statusBadgeColor(status: VolumeStatus): string {
    const map: Record<VolumeStatus, string> = {
      MONITORED: 'primary',
      COMPLETED: 'success',
      PAUSED:    'secondary'
    };
    return map[status];
  }

  progressPercent(volume: Volume): number {
    if (!volume.countOfIssues) return 0;
    return Math.round((volume.countOfDownloadedIssues / volume.countOfIssues) * 100);
  }

  volumeSubtitle(volume: Volume): string {
    const parts = [volume.year?.toString(), volume.publisher].filter((p): p is string => !!p);
    return parts.length ? parts.join(' · ') : '—';
  }

  ageRatingLabel(rating: AgeRating): string {
    return AGE_RATINGS.find(r => r.value === rating)?.label ?? rating;
  }

  synchronize(): void {
    const lib = this.library();
    if (!lib || this.syncing()) return;

    this.syncing.set(true);
    this.syncDone.set(null);
    this.error.set(null);

    this.libraryService.sync(lib.id)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: res => {
          this.syncDone.set(res.message);
          this.syncing.set(false);
        },
        error: err => {
          this.error.set(err?.error?.message ?? 'Failed to start synchronization.');
          this.syncing.set(false);
        }
      });
  }
}
