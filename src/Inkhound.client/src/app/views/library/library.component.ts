import { Component, DestroyRef, inject, signal } from '@angular/core';
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
import { Library, LibraryService } from '../../core/services/library.service';
import { KavitaService } from '../../core/services/kavita.service';
import { AGE_RATINGS, AgeRating, Volume, VolumeService, VolumeStatus } from '../../core/services/volume.service';
import { HubService } from '../../core/services/hub.service';
import { UpdatedData } from '../../core/models/hub.models';

@Component({
  selector: 'app-library',
  templateUrl: './library.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent, ButtonDirective, DatePipe, RouterLink,
    BadgeComponent, ProgressComponent, ProgressBarComponent, TooltipDirective
  ]
})
export class LibraryComponent {
  private route          = inject(ActivatedRoute);
  private libraryService = inject(LibraryService);
  private kavitaService  = inject(KavitaService);
  private volumeService  = inject(VolumeService);
  private hub            = inject(HubService);
  readonly #destroyRef   = inject(DestroyRef);

  library        = signal<Library | null>(null);
  loading        = signal(true);
  error          = signal<string | null>(null);
  syncing        = signal(false);
  syncDone       = signal<string | null>(null);
  volumes        = signal<Volume[]>([]);
  volumesLoading = signal(false);

  constructor() {
    this.kavitaService.loadLibraries();

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
