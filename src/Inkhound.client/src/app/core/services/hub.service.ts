import { inject, Injectable, signal } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { distinctUntilChanged } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { JobContext, StateServiceManager, TraceDefinition, UpdatedData } from '../models/hub.models';
import { AuthService } from './auth.service';

@Injectable({ providedIn: 'root' })
export class HubService {
  private auth = inject(AuthService);

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
      this.currentJob.set(job);
      this._jobs.update(list => {
        const idx = list.findIndex(j => j.jobId === job.jobId);
        if (idx >= 0) { const copy = [...list]; copy[idx] = job; return copy; }
        return [job, ...list];
      });
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

    this.connection.start().catch(console.error);
  }

  disconnect(): void {
    this.connection?.stop();
    this.connection = null;
    this.managerState.set(null);
    this.currentJob.set(null);
    this.lastTrace.set(null);
    this.lastDataUpdated.set(null);
    this._jobs.set([]);
    this._jobTraces.set(new Map());
  }
}
