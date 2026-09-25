import { inject, Injectable, signal } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { distinctUntilChanged } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { JobContext, StateServiceManager, TraceDefinition, UpdatedData } from '../models/hub.models';
import { AuthService } from './auth.service';
import { PageJobService } from './page-job.service';
import { JobsService } from './jobs.service';

@Injectable({ providedIn: 'root' })
export class HubService {
  private auth = inject(AuthService);
  private pageJobs = inject(PageJobService);
  private jobsApi = inject(JobsService);

  readonly managerState    = signal<StateServiceManager | null>(null);
  readonly currentJob      = signal<JobContext | null>(null);
  readonly lastTrace       = signal<TraceDefinition | null>(null);
  readonly lastDataUpdated = signal<UpdatedData | null>(null);

  private readonly _jobs      = signal<JobContext[]>([]);
  private readonly _jobTraces = signal<Map<string, TraceDefinition[]>>(new Map());
  readonly jobs      = this._jobs.asReadonly();
  readonly jobTraces = this._jobTraces.asReadonly();

  // Traces indexées par service, pour les modules qui tracent en continu hors de tout job (le
  // Chatbot tourne en boucle permanente : ses traces n'ont pas de jobId et n'entreraient donc
  // jamais dans _jobTraces). Historisées côté navigateur uniquement — rien n'est persisté serveur.
  private static readonly SERVICE_TRACE_LIMIT = 500;
  private static readonly SERVICE_TRACE_MAX_BYTES = 256_000;
  private static readonly TRACKED_SERVICES = new Set(['Chatbot']);
  private static readonly STORAGE_PREFIX = 'inkhound.serviceTraces.';
  private static readonly PERSIST_DEBOUNCE_MS = 1000;

  private readonly _serviceTraces = signal<Map<string, TraceDefinition[]>>(new Map());
  readonly serviceTraces = this._serviceTraces.asReadonly();

  private readonly persistTimers = new Map<string, ReturnType<typeof setTimeout>>();

  private connection: signalR.HubConnection | null = null;

  constructor() {
    // Réhydratation hors de connect() : l'historique doit être lisible même déconnecté.
    this.restoreServiceTraces();

    toObservable(this.auth.isAuthenticated).pipe(
      distinctUntilChanged()
    ).subscribe(authenticated => {
      authenticated ? this.connect() : this.disconnect();
    });
  }

  private connect(): void {
    if (this.connection) return;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hub/app', { accessTokenFactory: () => this.auth.getToken() ?? '' })
      .withAutomaticReconnect()
      .build();

    this.connection.on('ManagerStateChanged', (state: StateServiceManager) => {
      console.log('[Hub] ManagerStateChanged', state);
      this.managerState.set(state);
    });

    this.connection.on('ManagerHealthcheck', (state: StateServiceManager) => {
      console.log('[Hub] ManagerHealthcheck', state);
      this.managerState.set(state);
    });

    this.connection.on('ManagerJobChanged', (job: JobContext) => {
      console.log('[Hub] ManagerJobChanged', job);
      this.applyJobUpdate(job);
    });

    this.connection.on('ManagerTrace', (trace: TraceDefinition) => {
      console.log('[Hub] ManagerTrace', trace);
      this.lastTrace.set(trace);
      if (trace.jobId) {
        this._jobTraces.update(map => {
          const newMap = new Map(map);
          const existing = newMap.get(trace.jobId!) ?? [];
          newMap.set(trace.jobId!, [...existing, trace].slice(-100));
          return newMap;
        });
      }
      // Volontairement pas dans un `else` : une trace d'un service suivi doit rejoindre sa console
      // même si elle est par ailleurs rattachée à un job.
      if (trace.serviceName && HubService.TRACKED_SERVICES.has(trace.serviceName)) {
        this.appendServiceTrace(trace.serviceName, trace);
      }
    });

    this.connection.on('ManagerDataUpdated', (data: UpdatedData) => {
      console.log('[Hub] ManagerDataUpdated', data);
      this.lastDataUpdated.set(data);
    });

    // Sur mobile, l'app en arrière-plan coupe souvent le WebSocket ; un ManagerJobChanged émis
    // pendant cette fenêtre est perdu (broadcast fire-and-forget côté serveur, sans buffer). Ces
    // handlers + resyncTrackedJobs() rattrapent l'état via HTTP dès que la connexion revient.
    this.connection.onreconnected(() => this.resyncTrackedJobs());
    this.connection.onreconnecting(err => console.warn('[Hub] Reconnecting…', err));
    this.connection.onclose(err => console.warn('[Hub] Connection closed', err));

    this.connection.start()
      .then(() => this.resyncTrackedJobs())   // couvre aussi le cas F5 en cours de job
      .catch(err => {
        console.error('[Hub] Connection failed:', err);
        this.connection = null;
      });

    document.addEventListener('visibilitychange', this.handleVisibilityChange);
  }

  // Point d'écriture UNIQUE de currentJob/_jobs — appelé par l'event SignalR temps réel ET par la
  // resynchronisation HTTP, pour que le reste de l'app (computed()/effect() des pages métier) n'ait
  // à connaître qu'un seul mécanisme de mise à jour.
  private applyJobUpdate(job: JobContext): void {
    this.currentJob.set(job);
    this._jobs.update(list => {
      const idx = list.findIndex(j => j.jobId === job.jobId);
      if (idx >= 0) { const copy = [...list]; copy[idx] = job; return copy; }
      return [job, ...list];
    });
  }

  // Retour au premier plan mobile : si le reconnect automatique de SignalR a épuisé ses tentatives
  // pendant l'arrière-plan, la connexion est à l'état Disconnected — on la relance manuellement
  // avant de resynchroniser ; sinon on resynchronise directement (reconnexion déjà faite en silence).
  private handleVisibilityChange = (): void => {
    if (document.visibilityState !== 'visible' || !this.connection) return;

    if (this.connection.state === signalR.HubConnectionState.Disconnected) {
      this.connection.start()
        .then(() => this.resyncTrackedJobs())
        .catch(err => console.error('[Hub] Manual reconnect failed:', err));
    } else {
      this.resyncTrackedJobs();
    }
  };

  // Recoupe les jobs suivis par les pages (PageJobService) et ceux déjà connus mais non terminaux
  // (un job peut ne pas encore figurer dans jobs() si sa toute première mise à jour, INITIALIZING,
  // a elle-même été manquée), puis interroge leur état réel via GET /api/jobs/{id}.
  private resyncTrackedJobs(): void {
    const tracked = this.pageJobs.trackedEntries();
    const pending = this._jobs().filter(j => j.state === 'INITIALIZING' || j.state === 'RUNNING');
    const ids = new Set<string>([...tracked.map(t => t.jobId), ...pending.map(j => j.jobId)]);

    ids.forEach(jobId => {
      this.jobsApi.getStatus(jobId).subscribe({
        next: job => this.applyJobUpdate(job),
        error: err => {
          if (err?.status === 404) {
            // Job inconnu ou expiré (au-delà de JobRetention côté serveur) : son issue réelle
            // n'est plus connaissable ici — on libère la page pour éviter un blocage indéfini.
            const entry = tracked.find(t => t.jobId === jobId);
            if (entry) this.pageJobs.clear(entry.pageKey);
          } else {
            console.error('[Hub] Job resync failed:', jobId, err);
          }
        }
      });
    });
  }

  ensureConnected(): void {
    if (this.auth.isAuthenticated()) this.connect();
  }

  disconnect(): void {
    this.connection?.stop();
    this.connection = null;
    document.removeEventListener('visibilitychange', this.handleVisibilityChange);
    this.managerState.set(null);
    this.currentJob.set(null);
    this.lastTrace.set(null);
    this.lastDataUpdated.set(null);
    this._jobs.set([]);
    this._jobTraces.set(new Map());
    // Les traces de service ne sont PAS vidées : leur historique local survit à la déconnexion,
    // c'est tout l'intérêt de la console du module. clearServiceTraces() est le seul effacement.
  }

  // ── Traces par service (historique local) ──────────────────────────────────

  private appendServiceTrace(serviceName: string, trace: TraceDefinition): void {
    this._serviceTraces.update(map => {
      const newMap = new Map(map);
      const existing = newMap.get(serviceName) ?? [];
      newMap.set(serviceName, [...existing, trace].slice(-HubService.SERVICE_TRACE_LIMIT));
      return newMap;
    });
    this.schedulePersist(serviceName);
  }

  /** Vide la console d'un service, en mémoire et dans le stockage local. */
  clearServiceTraces(serviceName: string): void {
    this._serviceTraces.update(map => {
      const newMap = new Map(map);
      newMap.delete(serviceName);
      return newMap;
    });

    const timer = this.persistTimers.get(serviceName);
    if (timer) { clearTimeout(timer); this.persistTimers.delete(serviceName); }

    try {
      localStorage.removeItem(HubService.STORAGE_PREFIX + serviceName);
    } catch { /* stockage indisponible (navigation privée, site data bloqué) */ }
  }

  // Écriture débouncée : sérialiser 500 entrées à chaque message serait visible pendant une
  // commande bavarde.
  private schedulePersist(serviceName: string): void {
    const existing = this.persistTimers.get(serviceName);
    if (existing) clearTimeout(existing);

    this.persistTimers.set(serviceName, setTimeout(() => {
      this.persistTimers.delete(serviceName);
      this.persistServiceTraces(serviceName);
    }, HubService.PERSIST_DEBOUNCE_MS));
  }

  private persistServiceTraces(serviceName: string): void {
    const key = HubService.STORAGE_PREFIX + serviceName;
    let traces = this._serviceTraces().get(serviceName) ?? [];

    try {
      // Double borne : le nombre d'entrées ne dit rien de leur poids (une trace peut porter
      // plusieurs lignes de message). On retire par moitié jusqu'à tenir sous la limite d'octets.
      let payload = JSON.stringify(traces);
      while (payload.length > HubService.SERVICE_TRACE_MAX_BYTES && traces.length > 1) {
        traces = traces.slice(Math.ceil(traces.length / 2));
        payload = JSON.stringify(traces);
      }
      localStorage.setItem(key, payload);
    } catch (err) {
      // Quota dépassé ou stockage inaccessible : on purge et on continue — jamais d'erreur vers l'UI.
      console.warn('[Hub] Service trace persistence failed, clearing stored history', err);
      try { localStorage.removeItem(key); } catch { /* rien de plus à tenter */ }
    }
  }

  private restoreServiceTraces(): void {
    const restored = new Map<string, TraceDefinition[]>();

    HubService.TRACKED_SERVICES.forEach(serviceName => {
      try {
        const raw = localStorage.getItem(HubService.STORAGE_PREFIX + serviceName);
        if (!raw) return;
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed)) {
          restored.set(serviceName, parsed.slice(-HubService.SERVICE_TRACE_LIMIT));
        }
      } catch (err) {
        console.warn('[Hub] Could not restore service traces for', serviceName, err);
      }
    });

    if (restored.size > 0) this._serviceTraces.set(restored);
  }
}
