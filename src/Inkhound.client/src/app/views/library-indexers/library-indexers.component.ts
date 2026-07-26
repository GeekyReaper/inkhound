import { Component, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, forkJoin } from 'rxjs';
import {
  AlertComponent, BadgeComponent, ButtonDirective,
  CardBodyComponent, CardComponent, CardHeaderComponent,
  ColComponent, RowComponent,
  FormCheckComponent, FormCheckInputDirective, FormCheckLabelDirective,
  ModalBodyComponent, ModalComponent, ModalFooterComponent, ModalHeaderComponent,
  SpinnerComponent
} from '@coreui/angular';
import {
  IndexerSelection, ProwlarrIndexer, ProwlarrService, SelectedIndexer
} from '../../core/services/prowlarr.service';

@Component({
  selector: 'app-library-indexers',
  templateUrl: './library-indexers.component.html',
  imports: [
    RowComponent, ColComponent,
    CardComponent, CardHeaderComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent, ButtonDirective, BadgeComponent,
    FormCheckComponent, FormCheckInputDirective, FormCheckLabelDirective,
    ModalComponent, ModalHeaderComponent, ModalBodyComponent, ModalFooterComponent
  ]
})
export class LibraryIndexersComponent implements OnInit {
  libraryId = input.required<string>();

  private prowlarrService = inject(ProwlarrService);
  readonly #destroyRef    = inject(DestroyRef);

  indexers           = signal<ProwlarrIndexer[]>([]);
  selectedIds        = signal<Set<number>>(new Set());
  selectedCategories = signal<Map<number, Set<number>>>(new Map());
  activeIndexer      = signal<ProwlarrIndexer | null>(null);
  modalVisible       = signal(false);
  loading            = signal(true);
  saving             = signal(false);
  saveSuccess        = signal(false);
  error              = signal<string | null>(null);

  ngOnInit() {
    forkJoin({
      all:      this.prowlarrService.getIndexers(),
      selected: this.prowlarrService.getSelectedIndexers(this.libraryId())
    }).pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: ({ all, selected }) => {
          this.indexers.set(all);
          const ids = new Set(selected.map((s: SelectedIndexer) => s.indexerId));
          this.selectedIds.set(ids);

          const catMap = new Map<number, Set<number>>();
          for (const indexer of all) {
            if (ids.has(indexer.id)) {
              const savedIndexer = selected.find((s: SelectedIndexer) => s.indexerId === indexer.id);
              catMap.set(
                indexer.id,
                savedIndexer?.selectedCategoryIds?.length
                  ? new Set(savedIndexer.selectedCategoryIds)
                  : this.defaultCategories(indexer)
              );
            }
          }
          this.selectedCategories.set(catMap);
          this.loading.set(false);
        },
        error: err => {
          this.error.set(err?.error?.message ?? 'Failed to load indexers.');
          this.loading.set(false);
        }
      });
  }

  private defaultCategories(indexer: ProwlarrIndexer): Set<number> {
    const booksCats = indexer.categories.filter(c => c.name.toLowerCase().includes('books'));
    return new Set(booksCats.length > 0 ? booksCats.map(c => c.id) : []);
  }

  isSelected(id: number): boolean {
    return this.selectedIds().has(id);
  }

  toggle(indexer: ProwlarrIndexer): void {
    const ids    = new Set(this.selectedIds());
    const catMap = new Map(this.selectedCategories());

    if (ids.has(indexer.id)) {
      ids.delete(indexer.id);
      catMap.delete(indexer.id);
    } else {
      ids.add(indexer.id);
      catMap.set(indexer.id, this.defaultCategories(indexer));
    }

    this.selectedIds.set(ids);
    this.selectedCategories.set(catMap);
    this.saveSuccess.set(false);
  }

  openCategoriesModal(indexer: ProwlarrIndexer): void {
    this.activeIndexer.set(indexer);
    this.modalVisible.set(true);
  }

  closeCategoriesModal(): void {
    this.modalVisible.set(false);
    this.activeIndexer.set(null);
  }

  toggleCategory(indexerId: number, categoryId: number): void {
    const catMap = new Map(this.selectedCategories());
    const cats   = new Set(catMap.get(indexerId) ?? []);
    cats.has(categoryId) ? cats.delete(categoryId) : cats.add(categoryId);
    catMap.set(indexerId, cats);
    this.selectedCategories.set(catMap);
  }

  isCategorySelected(indexerId: number, categoryId: number): boolean {
    return this.selectedCategories().get(indexerId)?.has(categoryId) ?? false;
  }

  selectedCategoryCount(indexerId: number): number {
    return this.selectedCategories().get(indexerId)?.size ?? 0;
  }

  selectedCategoryNames(indexerId: number): string {
    const indexer = this.indexers().find(i => i.id === indexerId);
    if (!indexer) return '';
    const selected = this.selectedCategories().get(indexerId);
    if (!selected || selected.size === 0) return '';
    return indexer.categories
      .filter(c => selected.has(c.id))
      .map(c => c.name)
      .join(', ');
  }

  save(): void {
    const catMap = this.selectedCategories();
    const toSave: IndexerSelection[] = this.indexers()
      .filter(i => this.selectedIds().has(i.id))
      .map(i => ({
        id: i.id,
        name: i.name,
        protocol: i.protocol,
        enable: i.enable,
        selectedCategoryIds: [...(catMap.get(i.id) ?? [])]
      }));

    this.saving.set(true);
    this.saveSuccess.set(false);
    this.error.set(null);

    this.prowlarrService.setSelectedIndexers(this.libraryId(), toSave)
      .pipe(
        takeUntilDestroyed(this.#destroyRef),
        finalize(() => this.saving.set(false))
      )
      .subscribe({
        next:  () => this.saveSuccess.set(true),
        error: err => this.error.set(err?.error?.message ?? 'Save failed.')
      });
  }
}
