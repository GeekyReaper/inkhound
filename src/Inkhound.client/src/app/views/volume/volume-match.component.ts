import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AlertComponent,
  BadgeComponent,
  ButtonCloseDirective,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  ColComponent,
  ContainerComponent,
  FormControlDirective,
  FormLabelDirective,
  FormSelectDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  NavComponent,
  NavItemComponent,
  NavLinkDirective,
  PageItemComponent,
  PageLinkDirective,
  PaginationComponent,
  RowComponent,
  SpinnerComponent
} from '@coreui/angular';
import { NgClass, SlicePipe } from '@angular/common';
import { IconDirective } from '@coreui/icons-angular';
import {
  PageResult,
  UpdateIssueRequest,
  UpdateVolumeManuallyRequest,
  Volume,
  VolumeSearchResult,
  VolumeService
} from '../../core/services/volume.service';
import { ComicVineIssue, Issue, IssueService } from '../../core/services/issue.service';
import { ImageService } from '../../core/services/image.service';

@Component({
  selector: 'app-volume-match',
  templateUrl: './volume-match.component.html',
  styleUrl: './volume-add.component.scss',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardHeaderComponent, CardBodyComponent, CardFooterComponent,
    BadgeComponent,
    ModalComponent, ModalHeaderComponent, ModalBodyComponent, ModalTitleDirective, ButtonCloseDirective,
    SpinnerComponent, AlertComponent, ButtonDirective,
    PaginationComponent, PageItemComponent, PageLinkDirective,
    NavComponent, NavItemComponent, NavLinkDirective,
    FormControlDirective, FormLabelDirective, FormSelectDirective,
    FormsModule, ReactiveFormsModule, NgClass, IconDirective, SlicePipe
  ]
})
export class VolumeMatchComponent {
  private route         = inject(ActivatedRoute);
  private router        = inject(Router);
  private volumeService = inject(VolumeService);
  private issueService  = inject(IssueService);
  private imageService  = inject(ImageService);
  private fb            = inject(FormBuilder);
  readonly #destroyRef  = inject(DestroyRef);

  readonly volumeId      = this.route.snapshot.parent!.paramMap.get('volumeId') ?? '';
  readonly issuesPageSize = 12;

  readonly AUTHOR_ROLES = [
    'Writer', 'Penciler', 'Inker', 'Colorist',
    'Letterer', 'Editor', 'Artist', 'Translator'
  ];

  // ── Mode ──────────────────────────────────────────────────────────────────
  mode = signal<'comicvine' | 'manual'>('comicvine');

  // ── ComicVine state ───────────────────────────────────────────────────────
  query             = signal('');
  results           = signal<PageResult<VolumeSearchResult> | null>(null);
  cvLoading         = signal(false);
  cvError           = signal<string | null>(null);
  selectedSourceId  = signal<string | null>(null);
  rematching        = signal(false);
  issuesModalVolume = signal<VolumeSearchResult | null>(null);
  issuesPage        = signal<PageResult<ComicVineIssue> | null>(null);
  issuesLoading     = signal(false);
  issuesError       = signal<string | null>(null);

  // ── Manual form state ─────────────────────────────────────────────────────
  loading              = signal(true);
  saving               = signal(false);
  error                = signal<string | null>(null);
  volumeImagePreview   = signal<string | null>(null);
  uploadingVolumeImage = signal(false);
  issueImagePreviews   = signal<Record<number, string>>({});

  form = this.fb.group({
    title:       ['', Validators.required],
    year:        [null as number | null],
    publisher:   [''],
    description: [''],
    imageUrl:    [null as string | null],
    authors:     this.fb.array<FormGroup>([]),
    genres:      this.fb.array<FormGroup>([]),
    issues:      this.fb.array<FormGroup>([])
  });

  get authorsArray() { return this.form.get('authors') as FormArray; }
  get genresArray()  { return this.form.get('genres')  as FormArray; }
  get issuesArray()  { return this.form.get('issues')  as FormArray; }

  constructor() {
    this.volumeService.getById(this.volumeId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: vol => {
          this.populate(vol);
          this.loadVolumeIssues();
        },
        error: err => {
          this.error.set(err?.error?.message ?? 'Volume not found.');
          this.loading.set(false);
        }
      });
  }

  // ── ComicVine pagination ──────────────────────────────────────────────────
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

  // ── ComicVine methods ─────────────────────────────────────────────────────
  search(page = 1): void {
    const name = this.query().trim();
    if (!name) return;

    this.cvLoading.set(true);
    this.cvError.set(null);
    this.selectedSourceId.set(null);

    this.volumeService.search(name, page)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  res  => { this.results.set(res); this.cvLoading.set(false); },
        error: err  => { this.cvError.set(err?.error?.message ?? 'Search failed.'); this.cvLoading.set(false); }
      });
  }

  showIssues(volume: VolumeSearchResult, event: Event): void {
    event.stopPropagation();
    this.issuesPage.set(null);
    this.issuesError.set(null);
    this.issuesModalVolume.set(volume);
    this.loadCvIssues(volume, 1);
  }

  loadCvIssues(volume: VolumeSearchResult, page: number): void {
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

  onRematch(): void {
    const sourceId = this.selectedSourceId();
    if (!sourceId) return;

    this.rematching.set(true);
    this.cvError.set(null);

    this.volumeService.rematchFromComicVine(this.volumeId, sourceId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  () => this.router.navigate(['../'], { relativeTo: this.route }),
        error: err => {
          this.cvError.set(err?.error?.message ?? 'Rematch failed.');
          this.rematching.set(false);
        }
      });
  }

  // ── Manual form methods ───────────────────────────────────────────────────
  private populate(vol: Volume): void {
    this.form.patchValue({
      title:       vol.title,
      year:        vol.year,
      publisher:   vol.publisher ?? '',
      description: vol.description ?? '',
      imageUrl:    vol.image?.originalUrl ?? null
    });
    this.volumeImagePreview.set(vol.image?.smallUrl ?? null);
    vol.authors.forEach(a =>
      this.authorsArray.push(this.fb.group({ name: [a.name, Validators.required], role: [a.role] }))
    );
    vol.genres.forEach(g =>
      this.genresArray.push(this.fb.group({ value: [g, Validators.required] }))
    );
  }

  private loadVolumeIssues(): void {
    this.issueService.getByVolume(this.volumeId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: issues => {
          issues.sort((a, b) => a.issueNumber - b.issueNumber).forEach(iss => {
            const idx = this.issuesArray.length;
            this.issuesArray.push(this.buildIssueGroup(iss));
            const imgUrl = iss.image?.smallUrl ?? iss.image?.thumbUrl;
            if (imgUrl) {
              this.issueImagePreviews.update(prev => ({ ...prev, [idx]: imgUrl }));
            }
          });
          this.loading.set(false);
        },
        error: () => { this.loading.set(false); }
      });
  }

  private buildIssueGroup(iss?: Issue): FormGroup {
    return this.fb.group({
      id:          [iss?.id ?? null as string | null],
      issueNumber: [iss?.issueNumber ?? null as number | null, Validators.required],
      title:       [iss?.title ?? ''],
      year:        [iss?.year ?? null as number | null],
      description: [iss?.description ?? ''],
      imageUrl:    [iss?.image?.originalUrl ?? null as string | null],
      status:      [{ value: iss?.status ?? 'MISSING', disabled: true }]
    });
  }

  isDownloaded(ctrl: FormGroup): boolean {
    return ctrl.get('status')?.value === 'DOWNLOADED';
  }

  addAuthor(): void {
    this.authorsArray.push(this.fb.group({ name: ['', Validators.required], role: [''] }));
  }

  removeAuthor(i: number): void {
    this.authorsArray.removeAt(i);
  }

  addGenre(): void {
    this.genresArray.push(this.fb.group({ value: ['', Validators.required] }));
  }

  removeGenre(i: number): void {
    this.genresArray.removeAt(i);
  }

  addIssue(): void {
    this.issuesArray.push(this.buildIssueGroup());
  }

  removeIssue(i: number): void {
    this.issuesArray.removeAt(i);
    const previews = { ...this.issueImagePreviews() };
    delete previews[i];
    const reindexed: Record<number, string> = {};
    Object.keys(previews).forEach(k => {
      const idx = Number(k);
      reindexed[idx > i ? idx - 1 : idx] = previews[idx];
    });
    this.issueImagePreviews.set(reindexed);
  }

  onVolumeImageSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;

    this.uploadingVolumeImage.set(true);
    this.imageService.upload(file)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: res => {
          this.form.patchValue({ imageUrl: res.url });
          this.volumeImagePreview.set(res.url);
          this.uploadingVolumeImage.set(false);
        },
        error: err => {
          this.error.set(err?.error?.message ?? 'Image upload failed.');
          this.uploadingVolumeImage.set(false);
        }
      });
  }

  onIssueImageSelected(index: number, event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;

    this.imageService.upload(file)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: res => {
          (this.issuesArray.at(index) as FormGroup).patchValue({ imageUrl: res.url });
          this.issueImagePreviews.update(prev => ({ ...prev, [index]: res.url }));
        },
        error: err => {
          this.error.set(err?.error?.message ?? 'Image upload failed.');
        }
      });
  }

  onSubmitManual(): void {
    if (this.form.invalid) return;

    const val = this.form.getRawValue();
    const request: UpdateVolumeManuallyRequest = {
      title:       val.title ?? '',
      year:        val.year ?? null,
      publisher:   val.publisher || null,
      description: val.description || null,
      imageUrl:    val.imageUrl ?? null,
      authors:     (val.authors as { name: string; role: string }[]).map(a => ({ name: a.name, role: a.role ?? '' })),
      genres:      (val.genres as { value: string }[]).map(g => g.value),
      issues:      (val.issues as { id: string | null; issueNumber: number; title: string; year: number | null; description: string; imageUrl: string | null }[])
        .map(i => ({
          id:          i.id ?? null,
          issueNumber: i.issueNumber,
          title:       i.title || null,
          year:        i.year ?? null,
          description: i.description || null,
          imageUrl:    i.imageUrl ?? null
        } satisfies UpdateIssueRequest))
    };

    this.saving.set(true);
    this.error.set(null);
    this.volumeService.update(this.volumeId, request)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  () => this.router.navigate(['../'], { relativeTo: this.route }),
        error: err => {
          this.error.set(err?.error?.message ?? 'Failed to save volume.');
          this.saving.set(false);
        }
      });
  }

  cancel(): void {
    this.router.navigate(['../'], { relativeTo: this.route });
  }
}
