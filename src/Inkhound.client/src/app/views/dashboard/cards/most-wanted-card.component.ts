import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  BadgeComponent, CardBodyComponent, CardComponent, ProgressComponent, ProgressStackedComponent, TooltipDirective
} from '@coreui/angular';
import { DashboardMostWantedIssue } from '../../../core/services/dashboard.service';

// Carte d'une issue « Most wanted » (Dashboard + sous-page) : couverture, titre, et barre de
// complétude actuelle → projetée si l'issue est acquise.
@Component({
  selector: 'app-most-wanted-card',
  standalone: true,
  imports: [
    CardComponent, CardBodyComponent, BadgeComponent, ProgressComponent, ProgressStackedComponent,
    TooltipDirective, RouterLink
  ],
  template: `
    <c-card class="h-100" style="cursor: pointer;"
            [routerLink]="['/library', item().libraryId, 'volume', item().volumeId, 'issue', item().issueId]">
      <div style="aspect-ratio: 2/3; overflow: hidden; position: relative;">
        @if (cover(); as imgUrl) {
          <img [src]="imgUrl" [alt]="item().volumeTitle"
               style="width: 100%; height: 100%; object-fit: cover; object-position: top;" />
        } @else {
          <div class="w-100 h-100" style="background: var(--cui-secondary-bg); aspect-ratio: 2/3;"></div>
        }
        <c-badge color="danger" class="position-absolute top-0 end-0 m-1">Missing</c-badge>
      </div>
      <c-card-body class="p-2">
        <div class="small text-truncate" [title]="item().volumeTitle">
          {{ item().volumeTitle }} <span class="text-body-secondary">#{{ item().issueNumber }}</span>
        </div>
        <div [cTooltip]="tooltip()" cTooltipPlacement="top" class="mt-1">
          <c-progress-stacked style="height: 6px;">
            <c-progress [value]="item().currentCompletionPercent" [height]="6" color="success"></c-progress>
            <c-progress [value]="gainPercent()" [height]="6" color="warning"></c-progress>
          </c-progress-stacked>
          <div class="text-body-secondary" style="font-size: 11px;">
            {{ item().currentCompletionPercent }}% &rarr; {{ item().projectedCompletionPercent }}%
          </div>
        </div>
      </c-card-body>
    </c-card>
  `
})
export class MostWantedCardComponent {
  readonly item = input.required<DashboardMostWantedIssue>();

  cover(): string | null {
    return this.item().image?.smallUrl ?? this.item().image?.thumbUrl ?? null;
  }

  gainPercent(): number {
    return this.item().projectedCompletionPercent - this.item().currentCompletionPercent;
  }

  tooltip(): string {
    return `${this.item().ownedCount} / ${this.item().totalCount} owned · ${this.item().missingCount} missing`;
  }
}
