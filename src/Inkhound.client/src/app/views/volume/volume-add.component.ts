import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
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
import { NgClass, SlicePipe } from '@angular/common';
import { IconDirective } from '@coreui/icons-angular';
import { VolumeSearchResult, PageResult, VolumeService } from '../../core/services/volume.service';
import { ComicVineIssue, IssueService } from '../../core/services/issue.service';

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
    FormsModule, NgClass, IconDirective, SlicePipe
  ]
})
export class VolumeAddComponent {
  private route         = inject(ActivatedRoute);
  private router        = inject(Router);
  private volumeService = inject(VolumeService);
  private issueService  = inject(IssueService);
  readonly #destroyRef  = inject(DestroyRef);

  readonly libraryId      = this.route.snapshot.parent!.paramMap.get('id') ?? '';
  readonly issuesPageSize = 12;

  query            = signal('');
  results          = signal<PageResult<VolumeSearchResult> | null>(null);
  loading          = signal(false);
  error            = signal<string | null>(null);
  selectedSourceId = signal<string | null>(null);
  adding            = signal(false);
  issuesModalVolume = signal<VolumeSearchResult | null>(null);
  issuesPage        = signal<PageResult<ComicVineIssue> | null>(null);
  issuesLoading     = signal(false);
  issuesError       = signal<string | null>(null);

  visiblePages = computed(() => {
    const total   = this.results()?.totalPages ?? 0;
    const current = this.results()?.pageNumber ?? 1;
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
    const start = Math.max(1, Math.min(current - 3, total - 6));
    const end   = Math.min(total, start + 6);
    return Array.from({ length: end - start + 1 }, (_, i) => start + i);
  });

  issuesVisiblePages = computed(() => {
    const total   = this.issuesPage()?.totalPages ?? 0;
    const current = this.issuesPage()?.pageNumber ?? 1;
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
    this.issuesPage.set(null);
    this.issuesError.set(null);
    this.issuesModalVolume.set(volume);
    this.loadIssues(volume, 1);
  }

  loadIssues(volume: VolumeSearchResult, page: number): void {
    this.issuesLoading.set(true);
    this.issuesError.set(null);

    this.issueService.getByComicVineVolume(volume.sourceId, page, this.issuesPageSize)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  res => { this.issuesPage.set(res); this.issuesLoading.set(false); },
        error: err => { this.issuesError.set(err?.error?.message ?? 'Failed to load issues.'); this.issuesLoading.set(false); }
      });
  }

  closeIssuesModal(): void {
    this.issuesModalVolume.set(null);
    this.issuesPage.set(null);
    this.issuesError.set(null);
  }

  onAdd(): void {
    const sourceId = this.selectedSourceId();
    if (!sourceId) return;

    this.adding.set(true);
    this.volumeService.addFromComicVine(this.libraryId, sourceId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  () => this.router.navigate(['../'], { relativeTo: this.route }),
        error: err => {
          this.error.set(err?.error?.message ?? 'Failed to add volume.');
          this.adding.set(false);
        }
      });
  }
}
