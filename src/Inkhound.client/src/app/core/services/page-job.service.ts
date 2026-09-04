import { computed, Injectable, Signal, signal } from '@angular/core';

const STORAGE_KEY = 'inkhound_page_jobs';

// Associe l'URL résolue d'une page (ex: /library/{id}/volume/{volumeId}/issue/{issueId}) au
// jobId du Job qui y a été lancé, pour n'autoriser qu'un job actif par page et retrouver le bon
// jobId après un changement de vue. Persisté côté session uniquement (sessionStorage). Les jobs
// suivis ici sont resynchronisés via HTTP par HubService (trackedEntries()) à la reconnexion
// SignalR et au retour au premier plan — voir HubService.resyncTrackedJobs.
@Injectable({ providedIn: 'root' })
export class PageJobService {
  private readonly map = signal<Record<string, string>>(PageJobService.load());

  private static load(): Record<string, string> {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      return raw ? JSON.parse(raw) : {};
    } catch {
      return {};
    }
  }

  private persist(next: Record<string, string>): void {
    try { sessionStorage.setItem(STORAGE_KEY, JSON.stringify(next)); } catch { /* stockage indisponible */ }
  }

  register(pageKey: string, jobId: string): void {
    this.map.update(m => {
      const next = { ...m, [pageKey]: jobId };
      this.persist(next);
      return next;
    });
  }

  // À appeler explicitement par la page une fois le job terminé traité (résultat récupéré,
  // erreur affichée, etc.) pour libérer la page — pas d'auto-nettoyage implicite ici.
  clear(pageKey: string): void {
    this.map.update(m => {
      if (!(pageKey in m)) return m;
      const next = { ...m };
      delete next[pageKey];
      this.persist(next);
      return next;
    });
  }

  // Signal à créer UNE SEULE FOIS par page (champ de classe), pas à chaque appel dans un
  // template. Renvoie simplement le jobId enregistré, sans auto-invalidation sur l'état du job :
  // ce signal ne doit se dériver QUE de `map`, jamais de `hub.jobs()`. Sinon, dès qu'un job
  // passe à SUCCESS/ERROR, ce computed et celui du composant appelant (qui en dépend pour
  // détecter la fin du job) se recalculent tous les deux sur le même changement de hub.jobs() —
  // et rien ne garantit que le composant lise encore le jobId au moment où il traite cet état
  // terminal. La libération est uniquement explicite, via clear(), appelé par le composant une
  // fois la complétion effectivement traitée.
  activeJobId(pageKey: string): Signal<string | null> {
    return computed(() => this.map()[pageKey] ?? null);
  }

  // Snapshot de toutes les associations actives — permet une resynchronisation HTTP ciblée
  // (retour au premier plan, reconnexion) sans que l'appelant connaisse les pageKeys à l'avance.
  // Usage ponctuel/impératif (pas un Signal) : appelé uniquement au moment d'une resync, jamais
  // lu en continu dans un template.
  trackedEntries(): { pageKey: string; jobId: string }[] {
    return Object.entries(this.map()).map(([pageKey, jobId]) => ({ pageKey, jobId }));
  }
}
