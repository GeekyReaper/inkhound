import { Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { BadgeComponent, CardBodyComponent, CardComponent } from '@coreui/angular';
import { DashboardRecentVolume } from '../../../core/services/dashboard.service';

// Carte d'un volume récemment ajouté (Dashboard + sous-page « Recently added ») : couverture,
// titre, date d'ajout et badge de complétude (issues Standard téléchargées / total).
@Component({
  selector: 'app-recent-volume-card',
  standalone: true,
  imports: [CardComponent, CardBodyComponent, BadgeComponent, DatePipe, RouterLink],
  template: `
    <c-card class="h-100" style="cursor: pointer;" [routerLink]="['/library', volume().libraryId, 'volume', volume().id]">
      <div style="aspect-ratio: 2/3; overflow: hidden; position: relative;">
        @if (cover(); as imgUrl) {
          <img [src]="imgUrl" [alt]="volume().title"
               style="width: 100%; height: 100%; object-fit: cover; object-position: top;" />
        } @else {
          <div class="w-100 h-100" style="background: var(--cui-secondary-bg); aspect-ratio: 2/3;"></div>
        }
        <c-badge [color]="badgeColor()" class="position-absolute top-0 end-0 m-1">{{ volume().completionPercent }}%</c-badge>
      </div>
      <c-card-body class="p-2">
        <div class="small text-truncate" [title]="volume().title">{{ volume().title }}</div>
        <div class="text-body-secondary" style="font-size: 11px;">{{ volume().dateAdded | date:'mediumDate' }}</div>
      </c-card-body>
    </c-card>
  `
})
export class RecentVolumeCardComponent {
  readonly volume = input.required<DashboardRecentVolume>();

  readonly cover = computed(() => this.volume().image?.smallUrl ?? this.volume().image?.thumbUrl ?? null);

  readonly badgeColor = computed(() => {
    const pct = this.volume().completionPercent;
    return pct >= 100 ? 'success' : pct > 0 ? 'primary' : 'secondary';
  });
}
