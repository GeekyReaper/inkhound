import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
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
    BadgeComponent, ButtonDirective, SpinnerComponent, AlertComponent, TableDirective, IconDirective
  ],
  templateUrl: './system.component.html'
})
export class SystemSettingsComponent implements OnInit {
  private systemService = inject(SystemService);
  readonly #destroyRef  = inject(DestroyRef);

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
