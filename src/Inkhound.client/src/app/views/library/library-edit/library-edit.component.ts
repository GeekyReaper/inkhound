import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { switchMap } from 'rxjs';
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
  FormSelectDirective,
  InputGroupComponent,
  RowComponent,
  SpinnerComponent
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { Library, LibraryService, UpdateLibraryRequest } from '../../../core/services/library.service';
import { KavitaService } from '../../../core/services/kavita.service';
import { HubService } from '../../../core/services/hub.service';
import { SelectPathComponent } from '../../select-path/select-path.component';
import { LibraryIndexersComponent } from '../../library-indexers/library-indexers.component';

// Page /library/:id/edit — Name / Path (sélecteur de dossier) / Kavita library / Kavita path, puis
// le bloc Indexers (LibraryIndexersComponent, autonome : sauvegarde ses propres données). Seule
// page d'édition d'une library : /libraries n'est plus qu'une liste (Add = nom seul → redirige ici).
@Component({
  selector: 'app-library-edit',
  templateUrl: './library-edit.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardHeaderComponent, CardBodyComponent,
    ReactiveFormsModule, FormControlDirective, FormLabelDirective, FormSelectDirective,
    InputGroupComponent, ButtonDirective, SpinnerComponent, AlertComponent,
    IconDirective, RouterLink, SelectPathComponent, LibraryIndexersComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LibraryEditComponent {
  private route          = inject(ActivatedRoute);
  private router         = inject(Router);
  private libraryService = inject(LibraryService);
  private kavitaService  = inject(KavitaService);
  private hubService     = inject(HubService);
  readonly #destroyRef   = inject(DestroyRef);

  library   = signal<Library | null>(null);
  loading   = signal(true);
  loadError = signal<string | null>(null);

  kavitaServiceOk = computed(() =>
    this.hubService.managerState()?.stateServices
      .find(s => s.serviceName === 'Kavita')?.state === 'OK'
  );
  kavitaLibraries = this.kavitaService.libraries;
  kavitaLoading   = this.kavitaService.loading;

  pathPickerVisible = signal(false);
  saving    = signal(false);
  saveError = signal<string | null>(null);

  form = new FormGroup({
    name:            new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    path:            new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    kavitaLibraryId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    kavitaPath:      new FormControl('', { nonNullable: true })
  });

  get pathCtrl() { return this.form.controls.path; }

  constructor() {
    this.kavitaService.loadLibraries();

    // :id est porté par la route parente library/:id — pas d'héritage des params dans la config
    // du router (paramsInheritanceStrategy par défaut), d'où la lecture sur route.parent.
    this.route.parent!.paramMap
      .pipe(
        switchMap(params => this.libraryService.getById(params.get('id')!)),
        takeUntilDestroyed(this.#destroyRef)
      )
      .subscribe({
        next: lib => {
          this.library.set(lib);
          this.form.setValue({
            name:            lib.name,
            path:            lib.path,
            kavitaLibraryId: lib.kavitaLibraryId.toString(),
            kavitaPath:      lib.kavitaPath
          });
          this.loading.set(false);
        },
        error: err => {
          this.loadError.set(err?.error?.message ?? 'Library not found.');
          this.loading.set(false);
        }
      });
  }

  onPathSelected(path: string) {
    if (path) this.pathCtrl.setValue(path);
  }

  submit() {
    const lib = this.library();
    if (!lib) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const request: UpdateLibraryRequest = {
      name:            this.form.controls.name.value,
      path:            this.form.controls.path.value,
      kavitaLibraryId: Number(this.form.controls.kavitaLibraryId.value),
      kavitaPath:      this.form.controls.kavitaPath.value
    };

    this.saving.set(true);
    this.saveError.set(null);
    this.libraryService.update(lib.id, request)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: saved => {
          this.saving.set(false);
          // Sidebar (noms des libraries) alimentée par LibraryService.libraries
          this.libraryService.loadLibraries().pipe(takeUntilDestroyed(this.#destroyRef)).subscribe();
          this.router.navigate(['/library', saved.id]);
        },
        error: err => {
          this.saving.set(false);
          this.saveError.set(err?.error?.message ?? 'Failed to save library.');
        }
      });
  }
}
