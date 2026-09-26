import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { of } from 'rxjs';
import { map, switchMap } from 'rxjs/operators';
import {
  AlertComponent,
  ButtonCloseDirective,
  ButtonDirective,
  FormLabelDirective,
  FormSelectDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  SpinnerComponent
} from '@coreui/angular';
import { AGE_RATINGS, AgeRating, AgeRatingOption, SourceKey, VolumeService } from '../../../core/services/volume.service';
import { LibraryService } from '../../../core/services/library.service';

/** Volume créé par la dialog — le peuplement des issues continue dans le job `jobId`. */
export interface AddedVolume {
  id: string;
  jobId: string;
  libraryId: string;
}

// Modale « Add to Library » du workflow d'ajout classique : choix de la library (sauf si imposée),
// age rating optionnel, puis POST /api/libraries/{id}/volumes { source, sourceId } et PATCH de
// l'age rating. Partagée par la page Add Volume et par le module News — l'appelant décide de la
// suite (navigation, suivi du job) via l'output `added`.
@Component({
  selector: 'app-volume-add-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ModalComponent, ModalHeaderComponent, ModalBodyComponent, ModalFooterComponent, ModalTitleDirective,
    ButtonCloseDirective, ButtonDirective, FormLabelDirective, FormSelectDirective, SpinnerComponent, AlertComponent
  ],
  templateUrl: './volume-add-dialog.component.html'
})
export class VolumeAddDialogComponent {
  private readonly volumeService  = inject(VolumeService);
  private readonly libraryService = inject(LibraryService);
  readonly #destroyRef            = inject(DestroyRef);

  readonly visible  = input(false);
  readonly source   = input<SourceKey | null>(null);
  readonly sourceId = input<string | null>(null);
  /** Titre affiché en tête de la modale (série ajoutée). */
  readonly title    = input<string | null>(null);
  /** Library imposée par l'appelant (ex. ?library= de la page Add Volume) — masque le select. */
  readonly presetLibraryId = input<string | null>(null);

  readonly added  = output<AddedVolume>();
  readonly closed = output<void>();

  readonly libraries  = this.libraryService.libraries;
  readonly ageRatings: AgeRatingOption[] = AGE_RATINGS;

  readonly libraryId         = signal<string | null>(null);
  readonly selectedAgeRating = signal<AgeRating | ''>('');
  readonly adding            = signal(false);
  readonly error             = signal<string | null>(null);

  constructor() {
    if (this.libraries().length === 0) {
      this.libraryService.loadLibraries().pipe(takeUntilDestroyed(this.#destroyRef)).subscribe();
    }

    // Réinitialise la saisie à chaque ouverture ; une seule library → pré-sélectionnée.
    effect(() => {
      if (!this.visible()) return;
      const libs = this.libraries();
      this.libraryId.set(this.presetLibraryId() ?? (libs.length === 1 ? libs[0].id : null));
      this.selectedAgeRating.set('');
      this.error.set(null);
      this.adding.set(false);
    });
  }

  presetLibraryName(): string | null {
    const id = this.presetLibraryId();
    return id ? this.libraries().find(l => l.id === id)?.name ?? null : null;
  }

  cancel(): void {
    if (!this.adding()) this.closed.emit();
  }

  confirm(): void {
    const source = this.source();
    const sourceId = this.sourceId();
    const libraryId = this.libraryId();
    if (!source || !sourceId || !libraryId) return;

    this.adding.set(true);
    this.error.set(null);
    const ageRating = this.selectedAgeRating();

    this.volumeService.addFromSource(libraryId, source, sourceId)
      .pipe(
        switchMap(res => {
          const patch$ = ageRating ? this.volumeService.patchAgeRating(res.id, ageRating) : of(void 0);
          return patch$.pipe(map(() => res));
        }),
        takeUntilDestroyed(this.#destroyRef)
      )
      .subscribe({
        next: res => {
          this.adding.set(false);
          this.added.emit({ id: res.id, jobId: res.jobId, libraryId });
        },
        error: err => {
          this.error.set(err?.error?.message ?? 'Failed to add volume.');
          this.adding.set(false);
        }
      });
  }
}
