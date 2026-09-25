import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize, interval } from 'rxjs';
import {
  AlertComponent,
  BadgeComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
  ColComponent,
  RowComponent,
  SpinnerComponent,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { ChatbotService, ChatbotStatus } from '../../core/services/chatbot.service';
import { HubService } from '../../core/services/hub.service';
import { SmartDatePipe } from '../../core/pipes/smart-date.pipe';
import { TraceConsoleComponent } from '../trace-console/trace-console.component';
import { EState } from '../../core/models/hub.models';

/** Nom du service tel qu'il apparaît dans les traces et l'état du manager. */
const CHATBOT_SERVICE = 'Chatbot';

@Component({
  selector: 'app-chatbot-settings',
  standalone: true,
  imports: [
    RouterLink, SmartDatePipe, IconDirective,
    CardComponent, CardHeaderComponent, CardBodyComponent,
    RowComponent, ColComponent, ButtonDirective, BadgeComponent,
    AlertComponent, SpinnerComponent, TraceConsoleComponent,
  ],
  templateUrl: './chatbot.component.html',
  styleUrl: './chatbot.component.scss',
})
export class ChatbotSettingsComponent implements OnInit {
  private api        = inject(ChatbotService);
  private hub        = inject(HubService);
  private destroyRef = inject(DestroyRef);

  readonly status    = signal<ChatbotStatus | null>(null);
  readonly isLoading = signal(false);
  readonly isBusy    = signal(false);
  readonly error     = signal<string | null>(null);

  /** Historique local de la console — alimenté par SignalR, conservé par le navigateur seul. */
  readonly traces = computed(() => this.hub.serviceTraces().get(CHATBOT_SERVICE) ?? []);
  readonly traceLimit = 500;

  readonly serviceState = computed<EState | null>(() =>
    this.hub.managerState()?.stateServices.find(s => s.serviceName === CHATBOT_SERVICE)?.state ?? null);

  readonly stateColor = computed(() => {
    switch (this.serviceState()) {
      case 'OK':      return 'success';
      case 'WARNING': return 'warning';
      case 'ERROR':   return 'danger';
      case 'INVALID': return 'danger';
      default:        return 'secondary';
    }
  });

  readonly runningLabel = computed(() => {
    const s = this.status();
    if (!s) return '—';
    return s.running ? 'En cours' : 'Arrêté';
  });

  readonly runningColor = computed(() => this.status()?.running ? 'success' : 'secondary');

  readonly visionSummary = computed(() => {
    const s = this.status();
    if (!s?.visionProvider) return '—';
    const provider = s.visionProviders.find(p => p.name === s.visionProvider);
    return provider ? `${provider.name} — ${provider.model}` : s.visionProvider;
  });

  ngOnInit(): void {
    this.load();

    // Le statut ne passe pas par SignalR : un rafraîchissement régulier suffit pour voir bouger
    // le dernier sync et les compteurs.
    interval(10_000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load(true));
  }

  load(silent = false): void {
    if (!silent) this.isLoading.set(true);
    this.error.set(null);

    this.api.getStatus()
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.isLoading.set(false)))
      .subscribe({
        next: status => this.status.set(status),
        error: err => {
          if (!silent) this.error.set(err.error?.message ?? 'Impossible de lire l\'état du chatbot.');
        }
      });
  }

  start(): void {
    this.runAction(this.api.start());
  }

  stop(): void {
    this.runAction(this.api.stop());
  }

  clearTraces(): void {
    this.hub.clearServiceTraces(CHATBOT_SERVICE);
  }

  private runAction(request: ReturnType<ChatbotService['start']>): void {
    this.isBusy.set(true);
    this.error.set(null);

    request
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.isBusy.set(false)))
      .subscribe({
        next: status => this.status.set(status),
        error: err => this.error.set(err.error?.message ?? 'L\'action a échoué.')
      });
  }
}
