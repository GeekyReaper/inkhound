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

  private connection: signalR.HubConnection | null = null;

  constructor() {
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
  }
}
