import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import {
  AlertComponent,
  BadgeComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  ColComponent,
  ContainerComponent,
  ProgressBarComponent,
  ProgressComponent,
  RowComponent,
  TableDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { HubService } from '../../core/services/hub.service';
import { JobContext } from '../../core/models/hub.models';
import { JobConsoleModalComponent } from '../job-console-modal/job-console-modal.component';

@Component({
  selector: 'app-jobs',
  standalone: true,
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    BadgeComponent, ButtonDirective, AlertComponent, DatePipe, TableDirective,
    ProgressComponent, ProgressBarComponent,
    IconDirective,
    JobConsoleModalComponent,
  ],
  templateUrl: './jobs.component.html',
})
export class JobsComponent {
  private hub = inject(HubService);

  readonly jobs         = computed(() => this.hub.jobs());
  readonly selectedJob  = signal<JobContext | null>(null);
  readonly modalVisible = signal(false);

  openTraces(job: JobContext): void {
    this.selectedJob.set(job);
    this.modalVisible.set(true);
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

  progressColor(state: string): string {
    if (state === 'ERROR')   return 'danger';
    if (state === 'SUCCESS') return 'success';
    return 'primary';
  }
}
