import { Injectable, signal } from '@angular/core';
import { AgeRating } from './volume.service';

const STORAGE_KEY = 'inkhound_library_view';

// État de la vue liste d'une page Library : filtres côté client + page courante + position de
// scroll fenêtre. Un enregistrement par id de library.
export interface LibraryViewState {
  search:       string;
  letter:       string | null;
  completeness: 'all' | 'complete' | 'incomplete';
  source:       string | null;
  year:         number | null;
  ageRating:    AgeRating | null;
  page:         number;
  scrollY:      number;
}

export const EMPTY_LIBRARY_VIEW_STATE: LibraryViewState = {
  search: '', letter: null, completeness: 'all',
  source: null, year: null, ageRating: null, page: 1, scrollY: 0,
};

// Mémorise l'état de la vue liste de chaque page Library pour la durée de la session (sessionStorage,
// clé = id de library). Les filtres et la page sont restaurés à chaque retour sur la page (y compris
// lors d'un changement d'id sans destruction du composant) ; la position de scroll uniquement lors
// d'un back/forward navigateur (cf. LibraryComponent). Même approche que PageJobService : signal
// interne + persistance sessionStorage, accès protégés par try/catch (navigation privée / quota).
@Injectable({ providedIn: 'root' })
export class LibraryViewStateService {
  private readonly map = signal<Record<string, LibraryViewState>>(LibraryViewStateService.load());

  private static load(): Record<string, LibraryViewState> {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      return raw ? JSON.parse(raw) : {};
    } catch {
      return {};
    }
  }

  private persist(next: Record<string, LibraryViewState>): void {
    try { sessionStorage.setItem(STORAGE_KEY, JSON.stringify(next)); } catch { /* stockage indisponible */ }
  }

  get(libraryId: string): LibraryViewState | undefined {
    return this.map()[libraryId];
  }

  // Fusionne `patch` dans l'enregistrement existant (ou EMPTY_LIBRARY_VIEW_STATE) — permet de
  // sauver séparément les filtres/page et la position de scroll.
  patch(libraryId: string, patch: Partial<LibraryViewState>): void {
    this.map.update(m => {
      const next = {
        ...m,
        [libraryId]: { ...EMPTY_LIBRARY_VIEW_STATE, ...m[libraryId], ...patch },
      };
      this.persist(next);
      return next;
    });
  }
}
