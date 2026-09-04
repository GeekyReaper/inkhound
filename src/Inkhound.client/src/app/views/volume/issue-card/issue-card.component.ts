import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CardBodyComponent, CardComponent, CardFooterComponent } from '@coreui/angular';
import { Issue, IssueStatus } from '../../../core/services/issue.service';

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
  select = output<void>();

  statusBadgeClass = computed(() => {
    const map: Record<IssueStatus, string> = {
      DOWNLOADING: 'badge bg-info text-dark',
      DOWNLOADED:  'badge bg-success',
      MISSING:     'badge bg-danger'
    };
    return map[this.issue().status];
  });
}
