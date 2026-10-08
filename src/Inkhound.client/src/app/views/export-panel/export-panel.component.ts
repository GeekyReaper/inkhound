import { Component, DestroyRef, TemplateRef, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { filter, finalize } from 'rxjs';
import {
  AlertComponent, BadgeComponent, ButtonCloseDirective, ButtonDirective,
  CardBodyComponent, CardComponent, CardHeaderComponent,
  DropdownComponent, DropdownItemDirective, DropdownMenuDirective, DropdownToggleDirective,
  ModalBodyComponent, ModalComponent, ModalFooterComponent, ModalHeaderComponent, ModalTitleDirective,
  SpinnerComponent, TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import {
  ExportFile, ExportFormat, ExportService, ExportTargetType
} from '../../core/services/export.service';
import { HubService } from '../../core/services/hub.service';
import { PageJobService } from '../../core/services/page-job.service';
import { UpdatedData } from '../../core/models/hub.models';
import { SmartDatePipe } from '../../core/pipes/smart-date.pipe';
import { formatSize } from '../../core/util/format-size';
import { JobPanelComponent } from '../job-panel/job-panel.component';

// Carte « Download available » des pages Issue et Volume : suivi du job d'export et liste des fichiers déjà produits (téléchargement direct, suppression avec
// confirmation), affichée seulement s'il y a un fichier (ou un export en cours). Le bouton Download
// (choix PDF / CBZ) est exposé par `downloadButton` pour la barre d'actions de la page. Autonome : elle suit son propre job (clé de page dédiée) et se recharge sur les
// mises à jour SignalR des fichiers d'export (génération terminée, nettoyage planifié…).
@Component({
  selector: 'app-export-panel',
  standalone: true,
  imports: [
    CardComponent, CardHeaderComponent, CardBodyComponent,
    ButtonDirective, ButtonCloseDirective, BadgeComponent, AlertComponent, SpinnerComponent, TableDirective,
    DropdownComponent, DropdownToggleDirective, DropdownMenuDirective, DropdownItemDirective,
    ModalComponent, ModalHeaderComponent, ModalBodyComponent, ModalFooterComponent, ModalTitleDirective,
    IconDirective, SmartDatePipe, JobPanelComponent
  ],
  templateUrl: './export-panel.component.html'
})
export class ExportPanelComponent {
  private exportService = inject(ExportService);
  private hub           = inject(HubService);
  private pageJobs      = inject(PageJobService);
  private router        = inject(Router);
  readonly #destroyRef  = inject(DestroyRef);

  readonly targetType = input.required<ExportTargetType>();
  readonly targetId   = input.required<string>();

  /** Faux quand la cible n'a rien à exporter (issue sans fichier) : le bouton Download est désactivé. */
  readonly canExport = input(true);

  /** La page porte un autre job (import, refresh…) : Download est désactivé le temps qu'il tourne. */
  readonly blocked = input(false);

  /** Vrai tant qu'un export se lance ou tourne : la page désactive alors ses propres actions. */
  readonly busy = output<boolean>();

  readonly formatSize = formatSize;

  /** Bouton Download (menu PDF / CBZ), à rendre dans la barre d'actions de la page via ngTemplateOutlet. */
  readonly downloadButton = viewChild.required<TemplateRef<unknown>>('downloadButton');

  // Clé distincte de celle de la page : l'export ne bloque pas Import/Refresh et inversement.
  private readonly pageKey = `${this.router.url}#export`;
  readonly activeJobId = this.pageJobs.activeJobId(this.pageKey);
  private handledJobIds = new Set<string>();

  private readonly currentJob = computed(() => {
    const jobId = this.activeJobId();
    return jobId ? this.hub.jobs().find(j => j.jobId === jobId) ?? null : null;
  });

  exports       = signal<ExportFile[]>([]);
  defaultFormat = signal<ExportFormat>('Cbz');
  starting      = signal(false);
  error         = signal<string | null>(null);
  downloadingId = signal<string | null>(null);

  // --- Modale de confirmation de suppression ---
  deleteVisible = signal(false);
  deleteTarget  = signal<ExportFile | null>(null);
  deleting      = signal(false);
  deleteError   = signal<string | null>(null);

  readonly formats: ExportFormat[] = ['Pdf', 'Cbz'];

  constructor() {
    this.exportService.getDefaultFormat()
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({ next: f => this.defaultFormat.set(f), error: () => { /* repli : CBZ */ } });

    effect(() => this.busy.emit(!!this.activeJobId() || this.starting()));

    // Rechargement quand la cible change (navigation entre deux issues réutilisant le composant).
    effect(() => {
      this.targetType();
      this.targetId();
      untracked(() => this.load());
    });

    toObservable(this.hub.lastDataUpdated)
      .pipe(
        filter((d): d is UpdatedData => d !== null && d.dataType.endsWith('ExportFile')),
        takeUntilDestroyed(this.#destroyRef)
      )
      .subscribe(() => this.load());

    effect(() => {
      const job = this.currentJob();
      if (!job || this.handledJobIds.has(job.jobId)) return;
      if (job.state !== 'SUCCESS' && job.state !== 'ERROR') return;

      this.handledJobIds.add(job.jobId);
      this.pageJobs.clear(this.pageKey);
      if (job.state === 'ERROR') this.error.set('Export failed — see the job console for details.');
      // Les jobs n'émettent pas toujours un ManagerDataUpdated exploitable : rechargement explicite.
      untracked(() => this.load());
    });
  }

  formatLabel(format: ExportFormat): string {
    const name = format === 'Pdf' ? 'PDF' : 'CBZ';
    return this.targetType() === 'Volume' ? `ZIP of ${name}` : name;
  }

  fileKind(file: ExportFile): string {
    return file.targetType === 'Volume' ? `ZIP · ${file.format.toUpperCase()}` : file.format.toUpperCase();
  }

  load(): void {
    this.exportService.list(this.targetType(), this.targetId())
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: items => this.exports.set(items),
        error: () => { /* carte secondaire : un échec ne doit pas polluer la page */ }
      });
  }

  startExport(format: ExportFormat): void {
    if (this.activeJobId() || this.starting()) return;

    this.starting.set(true);
    this.error.set(null);

    const request = this.targetType() === 'Volume'
      ? this.exportService.exportVolume(this.targetId(), format)
      : this.exportService.exportIssue(this.targetId(), format);

    request
      .pipe(finalize(() => this.starting.set(false)), takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: res => {
          this.pageJobs.register(this.pageKey, res.jobId);
          this.load();   // fait apparaître la ligne « Generating… » (et retire l'ancien export remplacé)
        },
        error: err => {
          this.error.set(err?.error?.message ?? 'Failed to start the export.');
          this.load();
        }
      });
  }

  download(file: ExportFile): void {
    this.downloadingId.set(file.id);
    this.error.set(null);
    this.exportService.download(file.id)
      .pipe(finalize(() => this.downloadingId.set(null)), takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        error: err => {
          this.error.set(err?.error?.message ?? 'Download failed.');
          this.load();
        }
      });
  }

  // --- Suppression ---

  requestDelete(file: ExportFile): void {
    this.deleteError.set(null);
    this.deleteTarget.set(file);
    this.deleteVisible.set(true);
  }

  onDeleteVisibleChange(visible: boolean): void {
    if (!visible) this.closeDeleteModal();
  }

  closeDeleteModal(): void {
    if (this.deleting()) return;
    this.deleteVisible.set(false);
    this.deleteTarget.set(null);
  }

  confirmDelete(): void {
    const file = this.deleteTarget();
    if (!file || this.deleting()) return;

    this.deleting.set(true);
    this.deleteError.set(null);

    this.exportService.delete(file.id)
      .pipe(finalize(() => this.deleting.set(false)), takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: () => {
          this.deleteVisible.set(false);
          this.deleteTarget.set(null);
          this.load();
        },
        error: err => this.deleteError.set(err?.error?.message ?? 'Failed to delete the export.')
      });
  }
}
