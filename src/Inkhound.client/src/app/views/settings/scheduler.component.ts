import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize } from 'rxjs/operators';
import {
  AlertComponent, BadgeComponent, ButtonDirective,
  CardBodyComponent, CardComponent, CardHeaderComponent,
  ColComponent, ContainerComponent, RowComponent,
  FormCheckComponent, FormCheckInputDirective, FormCheckLabelDirective,
  FormControlDirective, FormDirective, FormFeedbackComponent, FormLabelDirective,
  SpinnerComponent
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import {
  SchedulerService, SchedulerStatus, SchedulerTaskKey
} from '../../core/services/scheduler.service';

// Cron 5 champs : validation légère côté client (5 groupes non vides séparés par des espaces).
// La validation réelle reste backend (Cronos).
const CRON_PATTERN = /^\S+(\s+\S+){4}$/;

@Component({
  selector: 'app-scheduler-settings',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardHeaderComponent, CardBodyComponent,
    FormDirective, FormControlDirective, FormLabelDirective, FormFeedbackComponent,
    FormCheckComponent, FormCheckInputDirective, FormCheckLabelDirective,
    BadgeComponent, ButtonDirective, SpinnerComponent, AlertComponent, IconDirective
  ],
  templateUrl: './scheduler.component.html'
})
export class SchedulerSettingsComponent implements OnInit {
  private schedulerService = inject(SchedulerService);
  readonly #destroyRef = inject(DestroyRef);

  status = signal<SchedulerStatus | null>(null);
  loading = signal(true);
  saving = signal(false);
  saveStatus = signal<'idle' | 'success' | 'error'>('idle');
  saveError = signal<string | null>(null);
  runningNow = signal<Set<SchedulerTaskKey>>(new Set());

  form = new FormGroup({
    processDownloadsEnabled: new FormControl(false, { nonNullable: true }),
    processDownloadsCron: new FormControl('', { nonNullable: true, validators: [Validators.pattern(CRON_PATTERN)] }),
    rollingRefreshEnabled: new FormControl(false, { nonNullable: true }),
    rollingRefreshCron: new FormControl('', { nonNullable: true, validators: [Validators.pattern(CRON_PATTERN)] }),
    rollingRefreshBatchSize: new FormControl(10, { nonNullable: true, validators: [Validators.required, Validators.min(1)] })
  });

  ngOnInit(): void {
    this.schedulerService.get()
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: s => this.applyStatus(s),
        error: () => this.saveError.set('Could not load the scheduler configuration.')
      });
  }

  save(): void {
    if (this.form.invalid || this.saving()) return;

    this.saving.set(true);
    this.saveStatus.set('idle');
    this.saveError.set(null);

    this.schedulerService.update(this.form.getRawValue())
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.saving.set(false)))
      .subscribe({
        next: s => { this.applyStatus(s); this.saveStatus.set('success'); },
        error: (err: HttpErrorResponse) => {
          this.saveStatus.set('error');
          this.saveError.set(err.error?.message ?? 'Failed to save the scheduler configuration.');
        }
      });
  }

  runNow(key: SchedulerTaskKey): void {
    if (this.runningNow().has(key)) return;
    this.runningNow.update(s => new Set(s).add(key));
    this.schedulerService.runNow(key)
      .pipe(
        takeUntilDestroyed(this.#destroyRef),
        finalize(() => this.runningNow.update(s => {
          const next = new Set(s);
          next.delete(key);
          return next;
        }))
      )
      .subscribe({ next: () => {}, error: () => {} });
  }

  // "—" si null, sinon date locale courte "dd/MM HH:mm".
  formatDate(value: string | null | undefined): string {
    if (!value) return '—';
    const d = new Date(value);
    if (Number.isNaN(d.getTime())) return '—';
    const pad = (n: number) => n.toString().padStart(2, '0');
    return `${pad(d.getDate())}/${pad(d.getMonth() + 1)} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  private applyStatus(s: SchedulerStatus): void {
    this.status.set(s);
    this.form.setValue({
      processDownloadsEnabled: s.processDownloads.enabled,
      processDownloadsCron: s.processDownloads.cron,
      rollingRefreshEnabled: s.rollingRefresh.enabled,
      rollingRefreshCron: s.rollingRefresh.cron,
      rollingRefreshBatchSize: s.rollingRefreshBatchSize
    });
  }
}
