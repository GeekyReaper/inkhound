import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
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
import { AgeRating, SourceKey } from '../../../core/services/volume.service';
import { LibraryService } from '../../../core/services/library.service';
import { AuthService } from '../../../core/services/auth.service';
import { VolumeRequestService } from '../../../core/services/volume-request.service';

/** Volume créé par la dialog — le peuplement des issues continue dans le job `jobId`. */
export interface AddedVolume {
  id: string;
  jobId: string | null;
  libraryId: string;
}

// Modale « Add to Library » du workflow d'ajout classique : choix de la library (sauf si imposée),
// age rating optionnel, puis POST /api/libraries/{id}/volumes { source, sourceId } et PATCH de
// l'age rating. Partagée par la page Add Volume et par le module News — l'appelant décide de la
// suite (navigation, suivi du job) via l'output `added`.
// Dans les deux cas la modale passe par POST /api/volume-requests : un admin obtient une demande
// auto-approuvée (volume créé, `jobId` pour suivre le peuplement des issues → output `added`), un
// Guest une demande en attente (age rating obligatoire → output `closed`).
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
  private readonly libraryService = inject(LibraryService);
  private readonly requestService = inject(VolumeRequestService);
  private readonly router         = inject(Router);
  private readonly auth           = inject(AuthService);
  readonly isAdmin                = this.auth.isAdmin;
  readonly #destroyRef            = inject(DestroyRef);

  readonly visible  = input(false);
  readonly source   = input<SourceKey | null>(null);
  readonly sourceId = input<string | null>(null);
  /** Titre affiché en tête de la modale (série ajoutée). */
  readonly title    = input<string | null>(null);
  /** Année et couverture de la série — recopiées dans la demande d'un Guest (aperçu de la liste). */
  readonly year     = input<number | null>(null);
  readonly image    = input<string | null>(null);
  /** Library imposée par l'appelant (ex. ?library= de la page Add Volume) — masque le select. */
  readonly presetLibraryId = input<string | null>(null);

  readonly added  = output<AddedVolume>();
  readonly closed = output<void>();

  readonly libraries  = this.libraryService.libraries;
  // Un compte plafonné ne voit pas les classifications au-delà de sa limite.
  readonly ageRatings = this.auth.allowedAgeRatings;

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

    // Admin : la demande est auto-approuvée (le volume est créé tout de suite, âge facultatif).
    // Guest : demande en attente, âge obligatoire.
    if (!this.isAdmin() && !ageRating) {
      this.error.set('Age rating is required.');
      this.adding.set(false);
      return;
    }

    this.requestService.create({
      libraryId, source, sourceId, ageRating: ageRating || 'Unknown',
      title: this.title() ?? '', year: this.year(), image: this.image()
    })
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: request => {
          this.adding.set(false);
          // Demande auto-approuvée (volume créé) : on rejoint la library, avec ou sans job à suivre.
          if (request.volumeId) {
            this.added.emit({ id: request.volumeId, jobId: request.jobId ?? null, libraryId });
            return;
          }
          this.closed.emit();
          // Depuis la page Requests la liste se rafraîchit seule (hub) ; ailleurs (News) on y conduit le Guest.
          if (!this.router.url.startsWith('/requests')) this.router.navigate(['/requests']);
        },
        error: err => {
          this.error.set(err?.error?.message
            ?? (this.isAdmin() ? 'Failed to add volume.' : 'Failed to send request.'));
          this.adding.set(false);
        }
      });
  }
}
