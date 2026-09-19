import { Component, computed, DestroyRef, effect, inject, OnInit, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize } from 'rxjs/operators';
import {
  AlertComponent, BadgeComponent, ButtonCloseDirective, ButtonDirective,
  CardBodyComponent, CardComponent, CardHeaderComponent,
  ColComponent, ContainerComponent, RowComponent,
  FormControlDirective, FormLabelDirective,
  ModalBodyComponent, ModalComponent, ModalFooterComponent, ModalHeaderComponent, ModalTitleDirective,
  SpinnerComponent, TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import {
  BedethequeCatalogService, BedethequeCatalogStatus, RefreshCatalogRequest
} from '../../core/services/bedetheque-catalog.service';
import { HubService } from '../../core/services/hub.service';
import { PageJobService } from '../../core/services/page-job.service';
import { SmartDatePipe } from '../../core/pipes/smart-date.pipe';
import { JobPanelComponent } from '../job-panel/job-panel.component';

// Page /settings/bedetheque — état du catalogue local des séries Bedetheque (27 lettres) et
// rafraîchissement manuel : N lettres les plus anciennes, tout l'index, ou une lettre précise.
// Le job est suivi via <app-job-panel> ; l'état est rechargé à sa fin (effect sur hub.jobs()).
@Component({
  selector: 'app-bedetheque-catalog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardHeaderComponent, CardBodyComponent,
    FormControlDirective, FormLabelDirective,
    ModalComponent, ModalHeaderComponent, ModalBodyComponent, ModalFooterComponent, ModalTitleDirective, ButtonCloseDirective,
    BadgeComponent, ButtonDirective, SpinnerComponent, AlertComponent, TableDirective, IconDirective,
    SmartDatePipe, JobPanelComponent
  ],
  templateUrl: './bedetheque-catalog.component.html',
  styleUrl: './bedetheque-catalog.component.scss'
})
export class BedethequeCatalogComponent implements OnInit {
  private catalogService = inject(BedethequeCatalogService);
  private hub            = inject(HubService);
  private pageJobs       = inject(PageJobService);
  readonly #destroyRef   = inject(DestroyRef);

  private readonly pageKey = '/settings/bedetheque';

  status  = signal<BedethequeCatalogStatus | null>(null);
  loading = signal(true);
  error   = signal<string | null>(null);
  launching = signal(false);
  confirmAllVisible = signal(false);

  // Nombre de lettres pour « Refresh oldest » (1-27).
  letterCount = new FormControl(3, { nonNullable: true, validators: [Validators.required, Validators.min(1), Validators.max(27)] });

  readonly activeJobId = this.pageJobs.activeJobId(this.pageKey);
  private handledJobIds = new Set<string>();

  private readonly currentJob = computed(() => {
    const jobId = this.activeJobId();
    return jobId ? this.hub.jobs().find(j => j.jobId === jobId) ?? null : null;
  });

  // Un refresh est en cours : job suivi par cette page, ou lancé ailleurs (scheduler / autre onglet).
  readonly refreshBusy = computed(() => !!this.activeJobId() || !!this.status()?.refreshRunning);

  readonly loadedLetters = computed(() => this.status()?.letters.filter(l => l.count > 0).length ?? 0);

  constructor() {
    effect(() => {
      const job = this.currentJob();
      if (!job || this.handledJobIds.has(job.jobId)) return;
      if (job.state === 'SUCCESS' || job.state === 'ERROR') {
        this.handledJobIds.add(job.jobId);
        this.pageJobs.clear(this.pageKey);
        this.load();
      }
    });
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.catalogService.getStatus()
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: s => { this.status.set(s); this.error.set(null); },
        error: (err: HttpErrorResponse) => this.error.set(err.error?.message ?? 'Could not load the catalog status.')
      });
  }

  refreshOldest(): void {
    if (this.letterCount.invalid) return;
    this.launch({ letterCount: this.letterCount.value });
  }

  requestRefreshAll(): void {
    this.confirmAllVisible.set(true);
  }

  confirmRefreshAll(): void {
    this.confirmAllVisible.set(false);
    this.launch({});
  }

  refreshLetter(letter: string): void {
    this.launch({ letters: [letter] });
  }

  private launch(request: RefreshCatalogRequest): void {
    if (this.launching() || this.refreshBusy()) return;

    this.launching.set(true);
    this.error.set(null);
    this.catalogService.refresh(request)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.launching.set(false)))
      .subscribe({
        next: res => this.pageJobs.register(this.pageKey, res.jobId),
        error: (err: HttpErrorResponse) => {
          this.error.set(err.error?.message ?? 'Failed to start the catalog refresh.');
          // 409 = un refresh tourne déjà (scheduler / autre onglet) : on resynchronise l'état.
          if (err.status === 409) this.load();
        }
      });
  }
}
