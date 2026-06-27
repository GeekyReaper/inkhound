import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
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
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  RowComponent,
  SpinnerComponent
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { AGE_RATINGS, AgeRating, AgeRatingOption, Volume, VolumeService, VolumeStatus } from '../../core/services/volume.service';
import { Issue, IssueService, IssueStatus } from '../../core/services/issue.service';
import { SelectPathComponent } from '../select-path/select-path.component';
import { HubService } from '../../core/services/hub.service';
import { UpdatedData } from '../../core/models/hub.models';

@Component({
  selector: 'app-volume',
  templateUrl: './volume.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent, CardFooterComponent,
    BadgeComponent,
    SpinnerComponent, AlertComponent, ButtonDirective, IconDirective,
    SelectPathComponent,
    ModalComponent, ModalHeaderComponent, ModalBodyComponent,
    ModalFooterComponent, ModalTitleDirective, ButtonCloseDirective
  ]
})
export class VolumeComponent {
  private route         = inject(ActivatedRoute);
  private router        = inject(Router);
  private volumeService = inject(VolumeService);
  private issueService  = inject(IssueService);
  private hub           = inject(HubService);
  readonly #destroyRef  = inject(DestroyRef);

  volume        = signal<Volume | null>(null);
  sourceLabel = computed(() => this.volume()?.sourceType === 'manual' ? 'Manual' : 'ComicVine');
  sourceColor = computed(() => this.volume()?.sourceType === 'manual' ? 'warning' : 'info');
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
  regenerating      = signal(false);
  regenerateSuccess = signal(false);

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

  onRegenerateComicInfo(): void {
    const vol = this.volume();
    if (!vol || this.regenerating()) return;
    this.regenerating.set(true);
    this.regenerateSuccess.set(false);
    this.volumeService.regenerateComicInfo(vol.id)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  () => { this.regenerating.set(false); this.regenerateSuccess.set(true); },
        error: err => { this.error.set(err?.error?.message ?? 'Regeneration failed.'); this.regenerating.set(false); }
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
