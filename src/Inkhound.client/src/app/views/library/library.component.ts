import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { switchMap } from 'rxjs';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  ColComponent,
  ContainerComponent,
  RowComponent,
  SpinnerComponent
} from '@coreui/angular';
import { Library, LibraryService } from '../../core/services/library.service';
import { KavitaService } from '../../core/services/kavita.service';
import { Volume, VolumeService, VolumeStatus } from '../../core/services/volume.service';

@Component({
  selector: 'app-library',
  templateUrl: './library.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent, ButtonDirective, DatePipe, RouterLink
  ]
})
export class LibraryComponent {
  private route          = inject(ActivatedRoute);
  private libraryService = inject(LibraryService);
  private kavitaService  = inject(KavitaService);
  private volumeService  = inject(VolumeService);
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

  statusBadgeClass(status: VolumeStatus): string {
    const map: Record<VolumeStatus, string> = {
      MONITORED: 'badge bg-primary',
      COMPLETED: 'badge bg-success',
      FREEZE:    'badge bg-secondary'
    };
    return map[status];
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
