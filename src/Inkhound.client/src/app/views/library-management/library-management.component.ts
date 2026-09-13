import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import {
  AlertComponent,
  ButtonCloseDirective,
  ButtonDirective,
  ColComponent,
  ContainerComponent,
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  FormControlDirective,
  FormLabelDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  RowComponent,
  SpinnerComponent,
  TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { Library, LibraryService } from '../../core/services/library.service';
import { KavitaService, KavitaLibrary } from '../../core/services/kavita.service';
import { SmartDatePipe } from '../../core/pipes/smart-date.pipe';

// Page /libraries : liste seule. « Add » ne demande qu'un nom (popup) puis redirige vers la page
// d'édition (/library/:id/edit) pour compléter Path / Kavita / indexers ; « Delete » (popup) propose
// de supprimer aussi les fichiers CBZ (répertoires des volumes) sur disque.
@Component({
  selector: 'app-library-management',
  templateUrl: './library-management.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    FormsModule, FormControlDirective, FormLabelDirective,
    FormCheckComponent, FormCheckInputDirective, FormCheckLabelDirective,
    ButtonDirective, ButtonCloseDirective, SpinnerComponent, AlertComponent, IconDirective,
    TableDirective, SmartDatePipe, RouterLink,
    ModalComponent, ModalHeaderComponent, ModalTitleDirective, ModalBodyComponent, ModalFooterComponent
  ]
})
export class LibraryManagementComponent implements OnInit {
  private libraryService = inject(LibraryService);
  private kavitaService  = inject(KavitaService);
  private router         = inject(Router);
  readonly #destroyRef   = inject(DestroyRef);

  libraries   = this.libraryService.libraries;
  loadingList = signal(false);
  listError   = signal<string | null>(null);

  getKavitaLibrary(id: number): KavitaLibrary | undefined {
    return this.kavitaService.libraries().find(l => l.id === id);
  }

  // ── Add (nom seul) ────────────────────────────────────────────────────────
  addModalVisible = signal(false);
  newName         = signal('');
  creating        = signal(false);
  createError     = signal<string | null>(null);

  // ── Delete ────────────────────────────────────────────────────────────────
  confirmDeleteVisible = signal(false);
  deleteTarget         = signal<Library | null>(null);
  deleteFiles          = signal(false);
  deleting             = signal(false);
  deleteError          = signal<string | null>(null);
  deleteWarning        = signal<string | null>(null);

  ngOnInit() {
    this.loadLibraries();
    this.kavitaService.loadLibraries();
  }

  private loadLibraries() {
    this.loadingList.set(true);
    this.listError.set(null);
    this.libraryService.loadLibraries()
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  () =>  this.loadingList.set(false),
        error: err => { this.listError.set(err?.error?.message ?? 'Failed to load libraries.'); this.loadingList.set(false); }
      });
  }

  openAdd() {
    this.newName.set('');
    this.createError.set(null);
    this.addModalVisible.set(true);
  }

  confirmAdd() {
    const name = this.newName().trim();
    if (!name || this.creating()) return;

    this.creating.set(true);
    this.createError.set(null);
    // Path / Kavita sont renseignés ensuite sur la page d'édition.
    this.libraryService.create({ name, path: '', kavitaLibraryId: 0, kavitaPath: '' })
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: lib => {
          this.creating.set(false);
          this.addModalVisible.set(false);
          this.libraryService.loadLibraries().pipe(takeUntilDestroyed(this.#destroyRef)).subscribe();
          this.router.navigate(['/library', lib.id, 'edit']);
        },
        error: err => {
          this.creating.set(false);
          this.createError.set(err?.error?.message ?? 'Failed to create library.');
        }
      });
  }

  requestDelete(lib: Library) {
    this.deleteTarget.set(lib);
    this.deleteFiles.set(false);
    this.deleteError.set(null);
    this.confirmDeleteVisible.set(true);
  }

  confirmDelete() {
    const lib = this.deleteTarget();
    if (!lib) return;

    this.deleting.set(true);
    this.deleteError.set(null);

    this.libraryService.delete(lib.id, this.deleteFiles())
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: res => {
          this.libraryService.loadLibraries().pipe(takeUntilDestroyed(this.#destroyRef)).subscribe();
          this.deleting.set(false);
          this.confirmDeleteVisible.set(false);
          this.deleteTarget.set(null);
          this.deleteWarning.set(res?.fileWarning ?? null);
        },
        error: (err) => {
          this.deleteError.set(err?.error?.message ?? 'Failed to delete library.');
          this.deleting.set(false);
        }
      });
  }
}
