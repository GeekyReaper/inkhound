import { Component, computed, DestroyRef, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { forkJoin, of, take } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  AlertComponent,
  ButtonCloseDirective,
  ButtonDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  SpinnerComponent
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { FilesystemService, DirectoryDto, FileDto } from '../../core/services/filesystem.service';

@Component({
  selector: 'app-select-path',
  templateUrl: './select-path.component.html',
  imports: [
    ModalComponent, ModalHeaderComponent, ModalTitleDirective, ButtonCloseDirective,
    ModalBodyComponent, ModalFooterComponent, ButtonDirective, SpinnerComponent,
    AlertComponent, IconDirective
  ]
})
export class SelectPathComponent {
  private fs = inject(FilesystemService);
  readonly #destroyRef = inject(DestroyRef);

  mode        = input<'file' | 'directory'>('directory');
  initialPath = input<string>('');
  visible     = model<boolean>(false);
  pathSelected = output<string>();

  history     = signal<string[]>(['.']);
  currentPath = computed(() => this.history().at(-1) ?? '.');
  directories = signal<DirectoryDto[]>([]);
  files       = signal<FileDto[]>([]);
  selectedPath      = signal('');
  loading           = signal(false);
  error             = signal<string | null>(null);
  pathInputInvalid  = signal(false);
  private pendingSelectPath = signal<string | null>(null);

  constructor() {
    effect(() => {
      if (this.visible()) {
        const start = this.initialPath()?.trim() || '.';
        this.history.set([start]);
        this.selectedPath.set('');
        this.error.set(null);
      }
    });

    effect(() => {
      const path = this.currentPath();
      if (!this.visible()) return;

      this.loading.set(true);
      this.error.set(null);
      this.pathInputInvalid.set(false);
      const pending = untracked(() => this.pendingSelectPath());
      this.selectedPath.set('');

      const dirs$ = this.fs.getDirectories(path).pipe(catchError(() => of([])));
      const files$ = this.mode() === 'file'
        ? this.fs.getFiles(path).pipe(catchError(() => of([])))
        : of([]);

      forkJoin({ dirs: dirs$, files: files$ })
        .pipe(takeUntilDestroyed(this.#destroyRef))
        .subscribe({
          next: ({ dirs, files }) => {
            this.directories.set(dirs as DirectoryDto[]);
            this.files.set(files as FileDto[]);
            this.loading.set(false);
            if (pending) {
              this.selectedPath.set(pending);
              this.pendingSelectPath.set(null);
            }
          },
          error: (err) => {
            this.error.set(err?.error?.message ?? 'Failed to load directory.');
            this.loading.set(false);
            this.pendingSelectPath.set(null);
          }
        });
    });
  }

  navigateInto(fullPath: string) {
    this.history.update(h => [...h, fullPath]);
  }

  navigateUp() {
    if (this.history().length > 1)
      this.history.update(h => h.slice(0, -1));
  }

  onDirectoryClick(dir: DirectoryDto) {
    if (this.mode() === 'directory')
      this.selectedPath.set(dir.fullPath);
    else
      this.navigateInto(dir.fullPath);
  }

  onDirectoryDblClick(dir: DirectoryDto) {
    this.navigateInto(dir.fullPath);
  }

  onFileClick(file: FileDto) {
    this.selectedPath.set(file.fullPath);
  }

  onFileDblClick(file: FileDto) {
    this.selectedPath.set(file.fullPath);
    this.confirm();
  }

  confirm() {
    this.pathSelected.emit(this.selectedPath());
    this.visible.set(false);
  }

  cancel() {
    this.pathSelected.emit('');
    this.visible.set(false);
  }

  onPathInputEnter(typedPath: string) {
    const trimmed = typedPath.trim();
    if (!trimmed) return;

    this.fs.getDirectories(trimmed)
      .pipe(take(1))
      .subscribe({
        next: () => {
          this.pathInputInvalid.set(false);
          this.pendingSelectPath.set(trimmed);
          this.navigateInto(trimmed);
        },
        error: () => {
          this.pathInputInvalid.set(true);
        }
      });
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
