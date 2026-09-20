import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CardBodyComponent, CardComponent, CardFooterComponent } from '@coreui/angular';
import { Issue, IssueStatus } from '../../../core/services/issue.service';
import { DownloadStatus } from '../../../core/services/qbittorrent.service';

// Mini-carte issue (cover, badge numéro, titre, année, nom de fichier, badge statut) — extraite
// de volume.component.html pour être réutilisée à l'identique dans le bloc "Issues" (Standard) et
// les sous-sections du bloc "Extra" (autres catégories).
@Component({
  selector: 'app-issue-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CardComponent, CardBodyComponent, CardFooterComponent],
  templateUrl: './issue-card.component.html'
})
export class IssueCardComponent {
  issue = input.required<Issue>();
  // Statut du téléchargement en cours, quand le parent l'a chargé (page Volume) — sert à
  // distinguer un téléchargement qui progresse d'un téléchargement bloqué.
  downloadStatus = input<DownloadStatus | null>(null);
  select = output<void>();

  // Un téléchargement bloqué (aucune source) n'avancera jamais : on le signale au lieu d'afficher
  // un DOWNLOADING rassurant. Libellé STALLED plutôt que DOWNLOADING en rouge, pour ne pas le
  // confondre avec le rouge de MISSING.
  readonly stalled = computed(() =>
    this.issue().status === 'DOWNLOADING' && this.downloadStatus() === 'Stalled');

  statusLabel = computed(() => this.stalled() ? 'STALLED' : this.issue().status);

  statusTitle = computed(() => this.stalled() ? 'Download stalled — no seeder' : '');

  statusBadgeClass = computed(() => {
    if (this.stalled()) return 'badge bg-danger';

    const map: Record<IssueStatus, string> = {
      DOWNLOADING: 'badge bg-info text-dark',
      DOWNLOADED:  'badge bg-success',
      MISSING:     'badge bg-danger'
    };
    return map[this.issue().status];
  });
}
