import { Component, computed, inject, input, model } from '@angular/core';
import {
  BadgeComponent,
  ButtonCloseDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
} from '@coreui/angular';
import { HubService } from '../../core/services/hub.service';
import { JobContext } from '../../core/models/hub.models';
import { TraceConsoleComponent } from '../trace-console/trace-console.component';

@Component({
  selector: 'app-job-console-modal',
  standalone: true,
  imports: [
    BadgeComponent,
    ModalComponent, ModalHeaderComponent, ModalTitleDirective,
    ModalBodyComponent, ButtonCloseDirective,
    TraceConsoleComponent,
  ],
  templateUrl: './job-console-modal.component.html',
})
export class JobConsoleModalComponent {
  private hub = inject(HubService);

  readonly job     = input<JobContext | null>(null);
  readonly visible = model(false);

  readonly traces = computed(() => {
    const job = this.job();
    if (!job) return [];
    return this.hub.jobTraces().get(job.jobId) ?? [];
  });

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
}
