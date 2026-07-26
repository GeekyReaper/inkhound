import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AlertComponent,
  ButtonCloseDirective,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
  ColComponent,
  ContainerComponent,
  FormControlDirective,
  FormLabelDirective,
  FormSelectDirective,
  InputGroupComponent,
  InputGroupTextDirective,
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
import { Library, LibraryService, CreateLibraryRequest, UpdateLibraryRequest } from '../../core/services/library.service';
import { KavitaService, KavitaLibrary } from '../../core/services/kavita.service';
import { HubService } from '../../core/services/hub.service';
import { SelectPathComponent } from '../select-path/select-path.component';
import { LibraryIndexersComponent } from '../library-indexers/library-indexers.component';

type PageMode = 'list' | 'add' | 'edit';

@Component({
  selector: 'app-library-management',
  templateUrl: './library-management.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardHeaderComponent, CardBodyComponent,
    ReactiveFormsModule, FormControlDirective, FormLabelDirective, FormSelectDirective,
    InputGroupComponent, InputGroupTextDirective,
    ButtonDirective, ButtonCloseDirective, SpinnerComponent, AlertComponent, IconDirective,
    TableDirective, SelectPathComponent, LibraryIndexersComponent, DatePipe,
    ModalComponent, ModalHeaderComponent, ModalTitleDirective, ModalBodyComponent, ModalFooterComponent
  ]
})
export class LibraryManagementComponent implements OnInit {
  private libraryService = inject(LibraryService);
  private kavitaService  = inject(KavitaService);
  private hubService     = inject(HubService);
  readonly #destroyRef = inject(DestroyRef);

  kavitaServiceOk = computed(() =>
    this.hubService.managerState()?.stateServices
      .find(s => s.serviceName === 'Kavita')?.state === 'OK'
  );
  kavitaLibraries = this.kavitaService.libraries;
  kavitaLoading   = this.kavitaService.loading;

  getKavitaLibrary(id: number): KavitaLibrary | undefined {
    return this.kavitaService.libraries().find(l => l.id === id);
  }

  mode = signal<PageMode>('list');

  libraries   = this.libraryService.libraries;
  loadingList = signal(false);
  listError   = signal<string | null>(null);

  pathPickerVisible   = signal(false);
  kavitaPickerVisible = signal(false);

  saving     = signal(false);
  saveStatus = signal<'idle' | 'success' | 'error'>('idle');
  saveError  = signal('');

  editingLibrary       = signal<Library | null>(null);
  confirmDeleteVisible = signal(false);
  deleteTarget         = signal<Library | null>(null);
  deleting             = signal(false);
  deleteError          = signal<string | null>(null);

  form = new FormGroup({
    name:            new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    path:            new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    kavitaLibraryId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    kavitaPath:      new FormControl('', { nonNullable: true })
  });

  get pathCtrl()      { return this.form.controls.path; }


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

  onPathSelected(path: string) {
    if (path) this.pathCtrl.setValue(path);
  }

  showAddForm() {
    this.editingLibrary.set(null);
    this.form.reset();
    this.saveStatus.set('idle');
    this.kavitaService.loadLibraries();
    this.mode.set('add');
  }

  showEditForm(lib: Library) {
    this.editingLibrary.set(lib);
    this.form.setValue({
      name:            lib.name,
      path:            lib.path,
      kavitaLibraryId: lib.kavitaLibraryId.toString(),
      kavitaPath:      lib.kavitaPath
    });
    this.saveStatus.set('idle');
    this.kavitaService.loadLibraries();
    this.mode.set('edit');
  }

  cancelAdd() {
    this.editingLibrary.set(null);
    this.mode.set('list');
  }

  requestDelete(lib: Library) {
    this.deleteTarget.set(lib);
    this.deleteError.set(null);
    this.confirmDeleteVisible.set(true);
  }

  confirmDelete() {
    const lib = this.deleteTarget();
    if (!lib) return;

    this.deleting.set(true);
    this.deleteError.set(null);

    this.libraryService.delete(lib.id)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: () => {
          this.libraryService.loadLibraries().pipe(takeUntilDestroyed(this.#destroyRef)).subscribe();
          this.deleting.set(false);
          this.confirmDeleteVisible.set(false);
          this.deleteTarget.set(null);
        },
        error: (err) => {
          this.deleteError.set(err?.error?.message ?? 'Failed to delete library.');
          this.deleting.set(false);
        }
      });
  }

  submit() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const request: CreateLibraryRequest = {
      name:            this.form.controls.name.value,
      path:            this.form.controls.path.value,
      kavitaLibraryId: Number(this.form.controls.kavitaLibraryId.value),
      kavitaPath:      this.form.controls.kavitaPath.value
    };

    this.saving.set(true);
    this.saveStatus.set('idle');

    if (this.mode() === 'edit') {
      const lib = this.editingLibrary()!;
      this.libraryService.update(lib.id, request as UpdateLibraryRequest)
        .pipe(takeUntilDestroyed(this.#destroyRef))
        .subscribe({
          next: () => {
            this.libraryService.loadLibraries().pipe(takeUntilDestroyed(this.#destroyRef)).subscribe();
            this.saving.set(false);
            this.editingLibrary.set(null);
            this.mode.set('list');
          },
          error: (err) => {
            this.saving.set(false);
            this.saveStatus.set('error');
            this.saveError.set(err?.error?.message ?? 'Failed to update library.');
          }
        });
    } else {
      this.libraryService.create(request)
        .pipe(takeUntilDestroyed(this.#destroyRef))
        .subscribe({
          next: () => {
            this.libraryService.loadLibraries().pipe(takeUntilDestroyed(this.#destroyRef)).subscribe();
            this.saving.set(false);
            this.mode.set('list');
          },
          error: (err) => {
            this.saving.set(false);
            this.saveStatus.set('error');
            this.saveError.set(err?.error?.message ?? 'Failed to create library.');
          }
        });
    }
  }
}
