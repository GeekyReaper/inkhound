import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
  ColComponent,
  ContainerComponent,
  FormControlDirective,
  FormLabelDirective,
  InputGroupComponent,
  InputGroupTextDirective,
  RowComponent,
  SpinnerComponent,
  TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { Library, LibraryService, CreateLibraryRequest } from '../../core/services/library.service';
import { SelectPathComponent } from '../select-path/select-path.component';

type PageMode = 'list' | 'add';

@Component({
  selector: 'app-library-management',
  templateUrl: './library-management.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardHeaderComponent, CardBodyComponent,
    ReactiveFormsModule, FormControlDirective, FormLabelDirective,
    InputGroupComponent, InputGroupTextDirective,
    ButtonDirective, SpinnerComponent, AlertComponent, IconDirective,
    TableDirective, SelectPathComponent, DatePipe
  ]
})
export class LibraryManagementComponent implements OnInit {
  private libraryService = inject(LibraryService);
  readonly #destroyRef = inject(DestroyRef);

  mode = signal<PageMode>('list');

  libraries   = signal<Library[]>([]);
  loadingList = signal(false);
  listError   = signal<string | null>(null);

  pathPickerVisible   = signal(false);
  kavitaPickerVisible = signal(false);

  saving     = signal(false);
  saveStatus = signal<'idle' | 'success' | 'error'>('idle');
  saveError  = signal('');

  form = new FormGroup({
    name:         new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    path:         new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    kavitaFolder: new FormControl('', { nonNullable: true, validators: [Validators.required] })
  });

  get pathCtrl()         { return this.form.controls.path; }
  get kavitaFolderCtrl() { return this.form.controls.kavitaFolder; }

  ngOnInit() {
    this.loadLibraries();
  }

  private loadLibraries() {
    this.loadingList.set(true);
    this.listError.set(null);

    this.libraryService.getAll()
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: (libs) => {
          this.libraries.set(libs);
          this.loadingList.set(false);
        },
        error: (err) => {
          this.listError.set(err?.error?.message ?? 'Failed to load libraries.');
          this.loadingList.set(false);
        }
      });
  }

  onPathSelected(path: string) {
    if (path) this.pathCtrl.setValue(path);
  }

  onKavitaFolderSelected(path: string) {
    if (path) this.kavitaFolderCtrl.setValue(path);
  }

  showAddForm() {
    this.form.reset();
    this.saveStatus.set('idle');
    this.mode.set('add');
  }

  cancelAdd() {
    this.mode.set('list');
  }

  submit() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const request: CreateLibraryRequest = {
      name:         this.form.controls.name.value,
      path:         this.form.controls.path.value,
      kavitaFolder: this.form.controls.kavitaFolder.value
    };

    this.saving.set(true);
    this.saveStatus.set('idle');

    this.libraryService.create(request)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: (created) => {
          this.saving.set(false);
          this.libraries.update(list => [...list, created]);
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
