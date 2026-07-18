import { Component, computed, effect, ElementRef, inject, input, model, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import {
  BadgeComponent,
  ButtonCloseDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
} from '@coreui/angular';
import { HubService } from '../../core/services/hub.service';
import { ETraceLevel, JobContext } from '../../core/models/hub.models';

@Component({
  selector: 'app-job-console-modal',
  standalone: true,
  imports: [
    DatePipe, BadgeComponent,
    ModalComponent, ModalHeaderComponent, ModalTitleDirective,
    ModalBodyComponent, ButtonCloseDirective,
  ],
  templateUrl: './job-console-modal.component.html',
  styleUrl: './job-console-modal.component.scss',
})
export class JobConsoleModalComponent {
  private hub = inject(HubService);
  private consoleEl = viewChild<ElementRef<HTMLDivElement>>('consoleEl');

  readonly job     = input<JobContext | null>(null);
  readonly visible = model(false);

  readonly traces = computed(() => {
    const job = this.job();
    if (!job) return [];
    return this.hub.jobTraces().get(job.jobId) ?? [];
  });

  constructor() {
    effect(() => {
      this.traces();
      const el = this.consoleEl();
      if (el) {
        el.nativeElement.scrollTop = el.nativeElement.scrollHeight;
      }
    });
  }

  close(): void {
    this.visible.set(false);
  }

  statusColor(state: string): string {
    switch (state) {
      case 'SUCCESS':      return 'success';
      case 'ERROR':        return 'danger';
      case 'RUNNING':      return 'primary';
      case 'INITIALIZING': return 'warning';
      default:             return 'secondary';
    }
  }

  traceLevelClass(level: ETraceLevel): string {
    switch (level) {
      case 'ERROR':    return 'trace-error';
      case 'CRITICAL': return 'trace-critical';
      case 'WARNING':  return 'trace-warning';
      case 'DEBUG':    return 'trace-debug';
      default:         return 'trace-info';
    }
  }
}
