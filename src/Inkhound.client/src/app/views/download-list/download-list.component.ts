import { Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { DecimalPipe } from '@angular/common';
import {
  AlertComponent, BadgeComponent, ButtonCloseDirective, ButtonDirective,
  FormControlDirective, FormLabelDirective,
  ModalBodyComponent, ModalComponent, ModalFooterComponent, ModalHeaderComponent, ModalTitleDirective,
  ProgressBarComponent, ProgressComponent,
  SpinnerComponent, TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { DownloadItem, QBittorrentService } from '../../core/services/qbittorrent.service';
import {
  canProcess, downloadStatusColor, formatAdded, formatEta, formatSize, formatSpeed
} from '../../core/util/download-format';

// Tableau des téléchargements et ses actions (process / correction de hash / suppression avec ses
// options « remove torrent » et « ban »), partagé par la page Downloads et la page Issue : une
// seule implémentation du rendu, de la carte mobile et du flux de suppression.
// Le parent garde ce qui lui est propre (filtres, pagination, polling, chargement) et réagit à
// `changed` pour recharger ses données.
@Component({
  selector: 'app-download-list',
  standalone: true,
  imports: [
    TableDirective, BadgeComponent, ButtonDirective, SpinnerComponent, AlertComponent,
    ProgressComponent, ProgressBarComponent, DecimalPipe, IconDirective,
    ModalComponent, ModalHeaderComponent, ModalBodyComponent, ModalFooterComponent,
    ModalTitleDirective, ButtonCloseDirective, FormControlDirective, FormLabelDirective
  ],
  templateUrl: './download-list.component.html',
  styleUrl: './download-list.component.scss'
})
export class DownloadListComponent {
  private qbService    = inject(QBittorrentService);
  readonly #destroyRef = inject(DestroyRef);

  readonly items = input.required<DownloadItem[]>();

  /** Masquer la colonne « Volume / Issue » — inutile quand la page porte déjà ce contexte (page Issue). */
  readonly showVolume = input(true);

  /** Une ligne a été supprimée / traitée / corrigée : le parent doit recharger sa liste. */
  readonly changed = output<void>();

  processingIds = signal<Set<string>>(new Set());

  // --- Modale « Fix download » (ligne NotFound : corriger le hash orphelin) ---
  editModalVisible = signal(false);
  editingItem      = signal<DownloadItem | null>(null);
  editHashInput    = signal('');
  editSaving       = signal(false);
  editError        = signal<string | null>(null);

  // --- Modale de confirmation de suppression (toute ligne, tout état) ---
  deleteModalVisible  = signal(false);
  deleteTarget        = signal<DownloadItem | null>(null);
  deleteRemoveTorrent = signal(true);
  // Bannit le couple (issue, torrent) : les recherches Prowlarr suivantes le scoreront 0.
  deleteBan           = signal(true);
  deleting            = signal(false);
  deleteError         = signal<string | null>(null);
  deleteNotice        = signal<string | null>(null);

  // Helpers de formatage partagés (core/util/download-format.ts).
  readonly statusBadgeColor = downloadStatusColor;
  readonly formatSpeed = formatSpeed;
  readonly formatEta = formatEta;
  readonly formatSize = formatSize;
  readonly formatAdded = formatAdded;
  readonly canProcess = canProcess;

  processOne(item: DownloadItem): void {
    if (this.processingIds().has(item.id)) return;
    this.processingIds.update(ids => new Set(ids).add(item.id));
    this.qbService.processDownload(item.id)
      .pipe(
        takeUntilDestroyed(this.#destroyRef),
        finalize(() => this.processingIds.update(ids => {
          const next = new Set(ids);
          next.delete(item.id);
          return next;
        }))
      )
      .subscribe({ next: () => {}, error: () => {} });
  }

  // --- Modale « Fix download » ---

  openEditModal(item: DownloadItem): void {
    this.editingItem.set(item);
    this.editHashInput.set('');
    this.editError.set(null);
    this.editModalVisible.set(true);
  }

  onEditModalVisibleChange(visible: boolean): void {
    if (!visible) this.closeEditModal();
  }

  closeEditModal(): void {
    this.editModalVisible.set(false);
    this.editingItem.set(null);
    this.editHashInput.set('');
    this.editError.set(null);
  }

  saveHash(): void {
    const item = this.editingItem();
    const hash = this.editHashInput().trim();
    if (!item || !hash || this.editSaving()) return;

    this.editSaving.set(true);
    this.editError.set(null);
    this.qbService.updateDownloadHash(item.id, hash)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.editSaving.set(false)))
      .subscribe({
        next:  () => { this.closeEditModal(); this.changed.emit(); },
        error: err => this.editError.set(err?.error?.message ?? 'Failed to update hash.')
      });
  }

  // --- Modale de confirmation de suppression ---

  requestDelete(item: DownloadItem): void {
    this.deleteTarget.set(item);
    this.deleteRemoveTorrent.set(true);
    this.deleteBan.set(true);
    this.deleteError.set(null);
    this.deleteNotice.set(null);
    this.deleteModalVisible.set(true);
  }

  onDeleteModalVisibleChange(visible: boolean): void {
    if (!visible) this.closeDeleteModal();
  }

  closeDeleteModal(): void {
    this.deleteModalVisible.set(false);
    this.deleteTarget.set(null);
    this.deleteError.set(null);
  }

  confirmDelete(): void {
    const item = this.deleteTarget();
    if (!item || this.deleting()) return;

    const removeTorrent = this.deleteRemoveTorrent();
    const ban = this.deleteBan();
    this.deleting.set(true);
    this.deleteError.set(null);
    this.qbService.deleteDownload(item.id, removeTorrent, ban)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.deleting.set(false)))
      .subscribe({
        next: res => {
          if (removeTorrent && !res.torrentRemoved) {
            this.deleteNotice.set('Tracking removed. The torrent could not be removed from qBittorrent (service unavailable) — only this download was deleted.');
          } else if (res.torrentRemoved && res.deletedCount > 1) {
            this.deleteNotice.set(`Torrent removed from qBittorrent — ${res.deletedCount} downloads that shared it were deleted.`);
          }
          this.closeDeleteModal();
          this.changed.emit();
        },
        error: err => this.deleteError.set(err?.error?.message ?? 'Failed to delete download.')
      });
  }

  dismissNotice(): void {
    this.deleteNotice.set(null);
  }
}
