import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AlertComponent,
  ButtonCloseDirective,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  ColComponent,
  ContainerComponent,
  ModalBodyComponent,
  ModalComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  PageItemComponent,
  PageLinkDirective,
  PaginationComponent,
  RowComponent,
  SpinnerComponent
} from '@coreui/angular';
import { NgClass } from '@angular/common';
import { VolumeSearchResult, PageResult, VolumeService } from '../../core/services/volume.service';

@Component({
  selector: 'app-volume-add',
  templateUrl: './volume-add.component.html',
  styleUrl: './volume-add.component.scss',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardHeaderComponent, CardBodyComponent, CardFooterComponent,
    ModalComponent, ModalHeaderComponent, ModalBodyComponent, ModalTitleDirective, ButtonCloseDirective,
    SpinnerComponent, AlertComponent, ButtonDirective,
    PaginationComponent, PageItemComponent, PageLinkDirective,
    FormsModule, NgClass
  ]
})
export class VolumeAddComponent {
  private route         = inject(ActivatedRoute);
  private volumeService = inject(VolumeService);
  readonly #destroyRef  = inject(DestroyRef);

  readonly libraryId = this.route.snapshot.parent!.paramMap.get('id') ?? '';

  query            = signal('');
  results          = signal<PageResult<VolumeSearchResult> | null>(null);
  loading          = signal(false);
  error            = signal<string | null>(null);
  selectedSourceId = signal<string | null>(null);
  issuesModalVolume = signal<VolumeSearchResult | null>(null);

  visiblePages = computed(() => {
    const total   = this.results()?.totalPages ?? 0;
    const current = this.results()?.pageNumber ?? 1;
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
    const start = Math.max(1, Math.min(current - 3, total - 6));
    const end   = Math.min(total, start + 6);
    return Array.from({ length: end - start + 1 }, (_, i) => start + i);
  });

  search(page = 1): void {
    const name = this.query().trim();
    if (!name) return;

    this.loading.set(true);
    this.error.set(null);
    this.selectedSourceId.set(null);

    this.volumeService.search(name, page)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  res  => { this.results.set(res); this.loading.set(false); },
        error: err  => { this.error.set(err?.error?.message ?? 'Search failed.'); this.loading.set(false); }
      });
  }

  showIssues(volume: VolumeSearchResult, event: Event): void {
    event.stopPropagation();
    this.issuesModalVolume.set(volume);
  }

  onAdd(): void {
    // Next step: persist the selected volume to the library
  }
}
