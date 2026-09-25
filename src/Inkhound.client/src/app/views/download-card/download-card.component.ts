import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BadgeComponent, CardBodyComponent, CardComponent } from '@coreui/angular';
import { DownloadItem } from '../../core/services/qbittorrent.service';
import { downloadStatusColor } from '../../core/util/download-format';

// Vignette d'un téléchargement : couverture de l'issue, titre du volume + numéro, début du nom du
// torrent, et badge de statut en surimpression. Utilisée par les deux sections du Dashboard
// (« Stalled downloads » et « Downloads in progress ») — même gabarit que les cartes « Most
// wanted » juste au-dessus, pour que le tableau de bord se lise d'un seul coup d'œil.
@Component({
  selector: 'app-download-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CardComponent, CardBodyComponent, BadgeComponent, RouterLink],
  templateUrl: './download-card.component.html'
})
export class DownloadCardComponent {
  readonly item = input.required<DownloadItem>();

  readonly cover = computed(() => this.item().coverUrl);

  // Titre affiché : celui du volume, à défaut le nom du torrent (download orphelin).
  readonly title = computed(() => this.item().volumeTitle ?? this.item().torrentTitle);

  // Lien vers la page de l'issue — null si volume/library inconnus (entité supprimée entre-temps) :
  // la carte reste affichée, simplement non cliquable.
  readonly link = computed<unknown[] | null>(() => {
    const i = this.item();
    return i.volumeId && i.libraryId
      ? ['/library', i.libraryId, 'volume', i.volumeId, 'issue', i.issueId]
      : null;
  });

  // Un téléchargement bloqué est une alerte : rouge, plutôt que l'orange générique du statut.
  readonly badgeColor = computed(() =>
    this.item().status === 'Stalled' ? 'danger' : downloadStatusColor(this.item().status));
}
