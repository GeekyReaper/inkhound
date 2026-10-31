import { Component, DestroyRef, computed, effect, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import {
  AlertComponent,
  BadgeComponent,
  ButtonDirective,
  ColComponent,
  FormSelectDirective,
  RowComponent,
  SpinnerComponent,
  TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { AuthService } from '../../core/services/auth.service';
import { HubService } from '../../core/services/hub.service';
import { LibraryService } from '../../core/services/library.service';
import { AGE_RATINGS, AgeRating, AgeRatingOption } from '../../core/services/volume.service';
import {
  VolumeRequest,
  VolumeRequestService,
  VolumeRequestStatus
} from '../../core/services/volume-request.service';
import { SmartDatePipe } from '../../core/pipes/smart-date.pipe';

// Section « Requests » de la page /requests : liste des demandes d'ajout de volume. Un Guest voit
// (et peut annuler tant qu'elles sont en attente) ses seules demandes — le périmètre est imposé par
// le serveur. L'admin voit par défaut les demandes en attente (boutons Pending / Accepted /
// Rejected, style de la page Downloads), peut changer la library cible et la classification d'âge d'une demande en attente, puis
// l'approuver (crée le volume) ou la refuser. Les ajouts directs d'un admin y figurent en Approved.
@Component({
  selector: 'app-volume-request-list',
  templateUrl: './volume-request-list.component.html',
  imports: [
    RowComponent, ColComponent, BadgeComponent, ButtonDirective,
    FormSelectDirective, SpinnerComponent, AlertComponent, IconDirective, TableDirective,
    SmartDatePipe, RouterLink
  ]
})
export class VolumeRequestListComponent implements OnInit {
  private readonly requestService = inject(VolumeRequestService);
  private readonly libraryService = inject(LibraryService);
  private readonly hub            = inject(HubService);
  readonly #destroyRef            = inject(DestroyRef);

  readonly isAdmin    = inject(AuthService).isAdmin;
  readonly libraries  = this.libraryService.libraries;
  readonly ageRatings: AgeRatingOption[] = AGE_RATINGS;

  readonly requests     = signal<VolumeRequest[]>([]);
  readonly loading      = signal(false);
  readonly error        = signal<string | null>(null);
  // Boutons radio Pending / Accepted / Rejected (style de la page Downloads), « Pending » par défaut.
  // Admin uniquement (file de traitement) ; un Guest voit toutes ses demandes, sans filtre affiché.
  readonly statusFilter = signal<VolumeRequestStatus>('Pending');
  /** Ids des demandes dont une action est en cours (boutons désactivés). */
  readonly busyIds      = signal<ReadonlySet<string>>(new Set());

  readonly filtered = computed(() => {
    if (!this.isAdmin()) return this.requests();
    const status = this.statusFilter();
    return this.requests().filter(r => r.status === status);
  });

  constructor() {
    // Une décision de l'admin (ou une nouvelle demande) change la liste : rechargement silencieux.
    effect(() => {
      const updated = this.hub.lastDataUpdated();
      if (updated?.dataType.endsWith('VolumeRequest')) this.load(false);
    });
  }

  ngOnInit(): void {
    if (this.libraries().length === 0) {
      this.libraryService.loadLibraries().pipe(takeUntilDestroyed(this.#destroyRef)).subscribe();
    }
    this.load(true);
  }

  libraryName(id: string): string {
    return this.libraries().find(l => l.id === id)?.name ?? '—';
  }

  ageRatingLabel(value: AgeRating): string {
    return this.ageRatings.find(r => r.value === value)?.label ?? value;
  }

  statusColor(status: VolumeRequestStatus): string {
    return status === 'Approved' ? 'success' : status === 'Rejected' ? 'danger' : 'warning';
  }

  isBusy(id: string): boolean {
    return this.busyIds().has(id);
  }

  setLibrary(request: VolumeRequest, libraryId: string): void {
    if (libraryId && libraryId !== request.libraryId)
      this.run(request, this.requestService.update(request.id, { libraryId }), 'Failed to update the request.');
  }

  setAgeRating(request: VolumeRequest, ageRating: string): void {
    if (ageRating && ageRating !== request.ageRating)
      this.run(request, this.requestService.update(request.id, { ageRating: ageRating as AgeRating }),
        'Failed to update the request.');
  }

  approve(request: VolumeRequest): void {
    this.run(request, this.requestService.approve(request.id), 'Failed to approve the request.');
  }

  reject(request: VolumeRequest): void {
    this.run(request, this.requestService.reject(request.id), 'Failed to reject the request.');
  }

  cancel(request: VolumeRequest): void {
    this.run(request, this.requestService.delete(request.id), 'Failed to cancel the request.');
  }

  private load(showSpinner: boolean): void {
    if (showSpinner) this.loading.set(true);
    this.requestService.getAll()
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: list => { this.requests.set(list); this.error.set(null); this.loading.set(false); },
        error: err => {
          this.error.set(err?.error?.message ?? 'Failed to load requests.');
          this.loading.set(false);
        }
      });
  }

  // Exécute une action sur une demande puis recharge la liste (l'état serveur fait foi).
  private run(request: VolumeRequest, action$: { subscribe: (o: object) => unknown }, failure: string): void {
    this.setBusy(request.id, true);
    this.error.set(null);
    action$.subscribe({
      next: () => { this.setBusy(request.id, false); this.load(false); },
      error: (err: { error?: { message?: string } }) => {
        this.error.set(err?.error?.message ?? failure);
        this.setBusy(request.id, false);
        this.load(false);
      }
    });
  }

  private setBusy(id: string, busy: boolean): void {
    this.busyIds.update(ids => {
      const next = new Set(ids);
      if (busy) next.add(id); else next.delete(id);
      return next;
    });
  }
}
