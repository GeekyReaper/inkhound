import { Component, computed, DestroyRef, effect, inject, OnInit, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize } from 'rxjs/operators';
import {
  AlertComponent, BadgeComponent, ButtonDirective,
  CardBodyComponent, CardComponent, CardHeaderComponent,
  ColComponent, ContainerComponent, RowComponent,
  SpinnerComponent, TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { HubService } from '../../core/services/hub.service';
import { PageJobService } from '../../core/services/page-job.service';
import { JobPanelComponent } from '../job-panel/job-panel.component';
import { SelectPathComponent } from '../select-path/select-path.component';
import { MemoryCompactionResult, MemorySnapshot, SystemService } from '../../core/services/system.service';

// Page /settings/system — empreinte mémoire du backend et purge manuelle.
// Volontairement sans rafraîchissement automatique : la mesure est ponctuelle, et un timer
// entretiendrait l'illusion d'un monitoring continu qu'Inkhound ne fait pas.
@Component({
  selector: 'app-system-settings',
  standalone: true,
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardHeaderComponent, CardBodyComponent,
    BadgeComponent, ButtonDirective, SpinnerComponent, AlertComponent, TableDirective, IconDirective,
    JobPanelComponent, SelectPathComponent
  ],
  templateUrl: './system.component.html'
})
export class SystemSettingsComponent implements OnInit {
  private systemService = inject(SystemService);
  private hub           = inject(HubService);
  private pageJobs      = inject(PageJobService);
  readonly #destroyRef  = inject(DestroyRef);

  private static readonly BACKUP_DIR_KEY = 'inkhound.backupDirectory';
  private readonly backupPageKey = 'settings-system#backup';
  private handledBackupJobIds = new Set<string>();

  // --- Sauvegarde de la base ---
  backupDirectory = signal(SystemSettingsComponent.readStoredDirectory());
  pathPickerVisible = signal(false);
  backupStarting  = signal(false);
  backupError     = signal<string | null>(null);
  backupResult    = signal<'success' | 'error' | null>(null);
  readonly backupJobId = this.pageJobs.activeJobId(this.backupPageKey);

  snapshot   = signal<MemorySnapshot | null>(null);
  loading    = signal(true);
  compacting = signal(false);
  error      = signal<string | null>(null);
  lastResult = signal<MemoryCompactionResult | null>(null);

  readonly totalCachedEntries = computed(
    () => this.snapshot()?.caches.reduce((sum, c) => sum + c.entryCount, 0) ?? 0);

  // Une limite mémoire de conteneur absente laisse le GC prendre la RAM de l'hôte pour budget :
  // c'est la cause première d'un RSS qui ne redescend jamais, on la signale explicitement.
  readonly containerLimitLooksUnset = computed(() => {
    const snap = this.snapshot();
    return !!snap && snap.totalAvailableMemoryBytes > 8 * 1024 * 1024 * 1024;
  });

  constructor() {
    // Fin du job de sauvegarde : on libère la page et on affiche le résultat (le détail est dans le job panel).
    effect(() => {
      const jobId = this.backupJobId();
      const job = jobId ? this.hub.jobs().find(j => j.jobId === jobId) ?? null : null;
      if (!job || this.handledBackupJobIds.has(job.jobId)) return;
      if (job.state !== 'SUCCESS' && job.state !== 'ERROR') return;

      this.handledBackupJobIds.add(job.jobId);
      untracked(() => {
        this.pageJobs.clear(this.backupPageKey);
        this.backupResult.set(job.state === 'SUCCESS' ? 'success' : 'error');
      });
    });
  }

  private static readStoredDirectory(): string {
    try { return localStorage.getItem(SystemSettingsComponent.BACKUP_DIR_KEY) ?? ''; } catch { return ''; }
  }

  onBackupPathSelected(path: string): void {
    this.backupDirectory.set(path);
    try { localStorage.setItem(SystemSettingsComponent.BACKUP_DIR_KEY, path); } catch { /* stockage indisponible */ }
  }

  backupDatabase(): void {
    const directory = this.backupDirectory().trim();
    if (!directory || this.backupStarting() || this.backupJobId()) return;

    this.backupStarting.set(true);
    this.backupError.set(null);
    this.backupResult.set(null);
    this.systemService.backupDatabase(directory)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.backupStarting.set(false)))
      .subscribe({
        next: res => this.pageJobs.register(this.backupPageKey, res.jobId),
        error: (err: HttpErrorResponse) => this.backupError.set(err.error?.message ?? 'Could not start the backup.')
      });
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.systemService.getMemory()
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: s => { this.snapshot.set(s); this.error.set(null); },
        error: (err: HttpErrorResponse) => this.error.set(err.error?.message ?? 'Could not read the memory snapshot.')
      });
  }

  compact(): void {
    if (this.compacting()) return;

    this.compacting.set(true);
    this.error.set(null);
    this.systemService.compactMemory()
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.compacting.set(false)))
      .subscribe({
        next: result => {
          this.lastResult.set(result);
          this.snapshot.set(result.after);
        },
        error: (err: HttpErrorResponse) => this.error.set(err.error?.message ?? 'The memory purge failed.')
      });
  }

  formatBytes(bytes: number): string {
    if (!Number.isFinite(bytes)) return '—';
    const mb = bytes / 1024 / 1024;
    return mb >= 1024 ? `${(mb / 1024).toFixed(2)} GB` : `${mb.toFixed(1)} MB`;
  }

  // Un gain négatif (le working set a monté entre les deux mesures) n'est pas une anomalie :
  // l'allocation continue pendant la compaction. On l'affiche tel quel plutôt que de le masquer.
  formatDelta(bytes: number): string {
    const sign = bytes > 0 ? '−' : bytes < 0 ? '+' : '';
    return `${sign}${this.formatBytes(Math.abs(bytes))}`;
  }
}
