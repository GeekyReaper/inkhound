import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { filter, finalize, switchMap } from 'rxjs';
import { DatePipe, SlicePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AlertComponent, BadgeComponent, ButtonDirective,
  CardBodyComponent, CardComponent,
  ColComponent, ContainerComponent,
  FormControlDirective, FormLabelDirective, FormSelectDirective,
  ModalModule,
  RowComponent, SpinnerComponent
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { Issue, IssueService, IssueStatus } from '../../core/services/issue.service';
import { HubService } from '../../core/services/hub.service';
import { PageJobService } from '../../core/services/page-job.service';
import { JobPanelComponent } from '../job-panel/job-panel.component';
import { ProwlarrSearchComponent } from '../prowlarr-search/prowlarr-search.component';
import { SelectPathComponent } from '../select-path/select-path.component';
import { UpdatedData } from '../../core/models/hub.models';

@Component({
  selector: 'app-issue',
  standalone: true,
  templateUrl: './issue.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent, BadgeComponent, ButtonDirective, IconDirective,
    ModalModule,
    FormControlDirective, FormLabelDirective, FormSelectDirective, ReactiveFormsModule,
    DatePipe, SlicePipe,
    JobPanelComponent,
    ProwlarrSearchComponent,
    SelectPathComponent
  ]
})
export class IssueComponent {
  private route              = inject(ActivatedRoute);
  private router             = inject(Router);
  private issueService       = inject(IssueService);
  private hub                = inject(HubService);
  private pageJobs           = inject(PageJobService);
  private fb                 = inject(FormBuilder);
  readonly #destroyRef       = inject(DestroyRef);

  readonly issueId = this.route.snapshot.paramMap.get('issueId')!;

  // Clé de page pour PageJobService (action "Analyze" — la recherche Prowlarr a sa propre clé,
  // gérée par ProwlarrSearchComponent).
  private readonly pageKey = this.router.url;

  readonly ISSUE_STATUSES: IssueStatus[] = ['MISSING', 'DOWNLOADING', 'DOWNLOADED'];

  issue         = signal<Issue | null>(null);
  loading       = signal(true);
  loadError     = signal<string | null>(null);
  analyzing     = signal(false);
  analyzeError  = signal<string | null>(null);

  // --- Import d'un fichier local comme CBZ de l'issue ---
  importVisible = signal(false);   // pioche du fichier (app-select-path mode="file")
  importing     = signal(false);
  importError   = signal<string | null>(null);

  // --- Suppression du fichier CBZ ---
  deleteFileModalVisible = signal(false);
  deletingFile           = signal(false);
  deleteFileError        = signal<string | null>(null);

  // --- État de la modale d'édition ---
  editModalVisible = signal(false);
  editSaving        = signal(false);
  editError         = signal<string | null>(null);

  editForm = this.fb.group({
    title:       [''],
    year:        [null as number | null],
    description: [''],
    status:      ['MISSING' as IssueStatus, Validators.required]
  });

  readonly activeJobId = this.pageJobs.activeJobId(this.pageKey);
  private handledJobIds = new Set<string>();

  private readonly currentJob = computed(() => {
    const jobId = this.activeJobId();
    return jobId ? this.hub.jobs().find(j => j.jobId === jobId) ?? null : null;
  });

  constructor() {
    this.issueService.getById(this.issueId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  issue => { this.issue.set(issue); this.loading.set(false); },
        error: ()    => { this.loadError.set('Issue not found.'); this.loading.set(false); }
      });

    toObservable(this.hub.lastDataUpdated)
      .pipe(
        filter((d): d is UpdatedData => d !== null && d.dataType.endsWith('Issue') && d.id === this.issueId),
        switchMap(() => this.issueService.getById(this.issueId)),
        takeUntilDestroyed(this.#destroyRef)
      )
      .subscribe(issue => this.issue.set(issue));

    effect(() => {
      const job = this.currentJob();
      if (!job || this.handledJobIds.has(job.jobId)) return;
      if (job.state !== 'SUCCESS' && job.state !== 'ERROR') return;

      this.handledJobIds.add(job.jobId);
      this.pageJobs.clear(this.pageKey);

      // Un seul job de page à la fois : le drapeau encore actif indique lequel vient de finir.
      const wasImport = this.importing();
      this.analyzing.set(false);
      this.importing.set(false);
      if (job.state === 'ERROR') {
        if (wasImport) this.importError.set('Import failed — see the console for details.');
        else this.analyzeError.set('Analysis failed — see the console for details.');
      }
      // Sinon, l'Issue mise à jour arrive via l'abonnement lastDataUpdated ci-dessus.
    });
  }

  goBack(): void {
    this.router.navigate(['.'], { relativeTo: this.route.parent });
  }

  // --- Actions de la modale d'édition ---

  openEditModal(): void {
    const issue = this.issue();
    if (!issue) return;

    this.editForm.setValue({
      title:       issue.title ?? '',
      year:        issue.year,
      description: issue.description ?? '',
      status:      issue.status
    });
    this.editError.set(null);
    this.editModalVisible.set(true);
  }

  onEditModalVisibleChange(visible: boolean): void {
    if (!visible) this.closeEditModal();
  }

  closeEditModal(): void {
    this.editModalVisible.set(false);
    this.editError.set(null);
  }

  onSaveEdit(): void {
    if (this.editForm.invalid) return;

    const val = this.editForm.getRawValue();
    this.editSaving.set(true);
    this.editError.set(null);

    this.issueService.update(this.issueId, {
      title:       val.title?.trim() || null,
      year:        val.year,
      description: val.description?.trim() || null,
      status:      val.status!
    })
      .pipe(
        takeUntilDestroyed(this.#destroyRef),
        finalize(() => this.editSaving.set(false))
      )
      .subscribe({
        next: () => {
          this.issueService.getById(this.issueId)
            .pipe(takeUntilDestroyed(this.#destroyRef))
            .subscribe(issue => this.issue.set(issue));
          this.closeEditModal();
        },
        error: err => this.editError.set(err?.error?.message ?? 'Failed to save issue.')
      });
  }

  onAnalyze(): void {
    if (this.activeJobId()) return;

    this.analyzing.set(true);
    this.analyzeError.set(null);

    this.issueService.analyze(this.issueId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  res => this.pageJobs.register(this.pageKey, res.jobId),
        error: err => { this.analyzeError.set(err?.error?.message ?? 'Analysis failed.'); this.analyzing.set(false); }
      });
  }

  // Fichier choisi dans le sélecteur → lance l'import (Job). L'issue passe DOWNLOADED en fin de job
  // et se rafraîchit via l'abonnement lastDataUpdated.
  onImportFileSelected(path: string): void {
    if (!path || this.activeJobId()) return;

    this.importing.set(true);
    this.importError.set(null);

    this.issueService.importFile(this.issueId, path)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  res => this.pageJobs.register(this.pageKey, res.jobId),
        error: err => { this.importError.set(err?.error?.message ?? 'Import failed.'); this.importing.set(false); }
      });
  }

  // --- Suppression du fichier CBZ ---

  requestDeleteFile(): void {
    this.deleteFileError.set(null);
    this.deleteFileModalVisible.set(true);
  }

  onDeleteFileModalVisibleChange(visible: boolean): void {
    if (!visible) this.closeDeleteFileModal();
  }

  closeDeleteFileModal(): void {
    this.deleteFileModalVisible.set(false);
    this.deleteFileError.set(null);
  }

  confirmDeleteFile(): void {
    if (this.deletingFile()) return;

    this.deletingFile.set(true);
    this.deleteFileError.set(null);

    this.issueService.deleteFile(this.issueId)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.deletingFile.set(false)))
      .subscribe({
        next: () => {
          this.issueService.getById(this.issueId)
            .pipe(takeUntilDestroyed(this.#destroyRef))
            .subscribe(i => this.issue.set(i));
          this.closeDeleteFileModal();
        },
        error: err => this.deleteFileError.set(err?.error?.message ?? 'Failed to delete the file.')
      });
  }

  scoreBandColor(band: string | null): string {
    switch (band) {
      case 'Excellent': return 'success';
      case 'Bon':        return 'info';
      case 'Correct':    return 'warning';
      case 'Faible':
      case 'Illisible':  return 'danger';
      default:           return 'secondary';
    }
  }

  formatBytes(bytes: number | null): string {
    if (bytes === null || bytes <= 0) return '—';
    if (bytes < 1024) return `${bytes.toFixed(0)} B`;
    if (bytes < 1_048_576) return `${(bytes / 1024).toFixed(1)} KB`;
    const mb = bytes / 1_048_576;
    return mb >= 1000 ? `${(mb / 1024).toFixed(2)} GB` : `${mb.toFixed(1)} MB`;
  }

  issueStatusBadgeClass(status: IssueStatus): string {
    const map: Record<IssueStatus, string> = {
      DOWNLOADING: 'badge bg-info text-dark',
      DOWNLOADED:  'badge bg-success',
      MISSING:     'badge bg-danger'
    };
    return map[status];
  }
}
