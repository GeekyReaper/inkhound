import { Injectable, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { JobContext, StateServiceManager, TraceDefinition } from '../models/hub.models';

@Injectable({ providedIn: 'root' })
export class HubService {
  readonly managerState = signal<StateServiceManager | null>(null);
  readonly currentJob   = signal<JobContext | null>(null);
  readonly lastTrace    = signal<TraceDefinition | null>(null);

  private connection: signalR.HubConnection | null = null;

  connect(token: string): void {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hub/app', { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .build();

    this.connection.on('ManagerStateChanged', (state: StateServiceManager) =>
      this.managerState.set(state));

    this.connection.on('ManagerHealthcheck', (state: StateServiceManager) =>
      this.managerState.set(state));

    this.connection.on('ManagerJobChanged', (job: JobContext) =>
      this.currentJob.set(job));

    this.connection.on('ManagerTrace', (trace: TraceDefinition) =>
      this.lastTrace.set(trace));

    this.connection.start().catch(console.error);
  }

  disconnect(): void {
    this.connection?.stop();
    this.connection = null;
  }
}
