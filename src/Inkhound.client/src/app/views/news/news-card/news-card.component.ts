import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { BadgeComponent, ButtonDirective, CardBodyComponent, CardComponent, CardFooterComponent, SpinnerComponent } from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { NEWS_CATEGORIES, NewsItem, newsItemLabel } from '../../../core/services/news.service';

// Vignette d'un item News (top ventes ou nouveauté) : couverture cliquable (page détail), rang +
// évolution pour le top ventes, série / n° / titre, éditeur et date de sortie, résumé, puis « Add »
// (workflow d'ajout classique, géré par la page ; grisé si la série est déjà en bibliothèque) et une
// loupe qui agrandit la couverture (visionneuse avec bouton « Details »).
@Component({
  selector: 'app-news-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CardComponent, CardBodyComponent, CardFooterComponent, BadgeComponent, ButtonDirective, SpinnerComponent, IconDirective, RouterLink, DatePipe],
  templateUrl: './news-card.component.html',
  styleUrl: './news-card.component.scss'
})
export class NewsCardComponent {
  readonly item   = input.required<NewsItem>();
  /** Ajout en cours pour cet item (résolution de la série / appel API). */
  readonly busy   = input(false);

  readonly zoom = output<NewsItem>();
  readonly add  = output<NewsItem>();

  readonly label = computed(() => newsItemLabel(this.item()));

  readonly categoryLabel = computed(() => {
    const c = this.item().category;
    return c ? NEWS_CATEGORIES.find(x => x.value === c)?.label ?? c : null;
  });

  readonly libraryLink = computed<unknown[] | null>(() => {
    const lib = this.item().library;
    return lib ? ['/library', lib.libraryId, 'volume', lib.volumeId] : null;
  });

  readonly evolutionColor = computed(() => {
    switch (this.item().evolution) {
      case 'New':  return 'warning';
      case 'Up':   return 'success';
      case 'Down': return 'danger';
      default:     return 'secondary';
    }
  });

  readonly evolutionText = computed(() => {
    const i = this.item();
    switch (i.evolution) {
      case 'New':  return 'New';
      case 'Up':   return `▲ ${i.evolutionDelta ?? ''}`.trim();
      case 'Down': return `▼ ${i.evolutionDelta ?? ''}`.trim();
      case 'Stable': return '=';
      default:     return null;
    }
  });
}
