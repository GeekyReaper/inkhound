import { Injectable, signal } from '@angular/core';
import { NewsCategory } from './news.service';

const STORAGE_KEY = 'inkhound_news_view';

export type NewsTab = 'top-sales' | 'releases';

// État de la page News : onglet actif, semaine affichée du top ventes, filtres + page des
// nouveautés, et position de scroll fenêtre propre à chaque onglet.
export interface NewsViewState {
  tab:             NewsTab;
  topSalesPeriod:  string | null;
  category:        NewsCategory | null;
  month:           string | null;
  page:            number;
  scrollTopSales:  number;
  scrollReleases:  number;
}

export const EMPTY_NEWS_VIEW_STATE: NewsViewState = {
  tab: 'top-sales', topSalesPeriod: null, category: null, month: null, page: 1,
  scrollTopSales: 0, scrollReleases: 0,
};

// Mémorise l'état de la page News pour la durée de la session (sessionStorage) — la page est
// détruite à l'ouverture d'un détail d'album. Onglet et filtres sont restaurés à chaque arrivée ;
// le scroll uniquement lors d'un « retour » (cf. NewsComponent / NavigationTrackerService). Même
// approche que LibraryViewStateService.
@Injectable({ providedIn: 'root' })
export class NewsViewStateService {
  private readonly state = signal<NewsViewState>(NewsViewStateService.load());

  private static load(): NewsViewState {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      return raw ? { ...EMPTY_NEWS_VIEW_STATE, ...JSON.parse(raw) } : EMPTY_NEWS_VIEW_STATE;
    } catch {
      return EMPTY_NEWS_VIEW_STATE;
    }
  }

  get(): NewsViewState {
    return this.state();
  }

  // Fusionne `patch` dans l'état courant — filtres et scroll sont sauvés séparément.
  patch(patch: Partial<NewsViewState>): void {
    this.state.update(s => {
      const next = { ...s, ...patch };
      try { sessionStorage.setItem(STORAGE_KEY, JSON.stringify(next)); } catch { /* stockage indisponible */ }
      return next;
    });
  }
}
