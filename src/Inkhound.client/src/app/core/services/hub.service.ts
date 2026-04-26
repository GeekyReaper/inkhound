import { effect, inject, Injectable, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { JobContext, StateServiceManager, TraceDefinition } from '../models/hub.models';
import { AuthService } from './auth.service';

@Injectable({ providedIn: 'root' })
export class HubService {
  private auth = inject(AuthService);

  readonly managerState = signal<StateServiceManager | null>(null);
  readonly currentJob   = signal<JobContext | null>(null);
  readonly lastTrace    = signal<TraceDefinition | null>(null);

  private connection: signalR.HubConnection | null = null;

  constructor() {
    effect(() => {
      if (this.auth.isAuthenticated()) {
        this.connect();
      } else {
        this.disconnect();
      }
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
    });

    this.connection.on('ManagerTrace', (trace: TraceDefinition) => {
      console.log('[Hub] ManagerTrace', trace);
      this.lastTrace.set(trace);
    });

    this.connection.start().catch(console.error);
  }

  disconnect(): void {
    this.connection?.stop();
    this.connection = null;
  }
}
