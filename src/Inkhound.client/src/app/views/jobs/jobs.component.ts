import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import {
  AlertComponent,
  BadgeComponent,
  ButtonCloseDirective,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  ColComponent,
  ContainerComponent,
  ModalBodyComponent,
  ModalComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  ProgressBarComponent,
  ProgressComponent,
  RowComponent,
  TableDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { HubService } from '../../core/services/hub.service';
import { ETraceLevel, JobContext } from '../../core/models/hub.models';

@Component({
  selector: 'app-jobs',
  standalone: true,
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    BadgeComponent, ButtonDirective, AlertComponent, DatePipe, TableDirective,
    ProgressComponent, ProgressBarComponent,
    ModalComponent, ModalHeaderComponent, ModalTitleDirective,
    ModalBodyComponent, ButtonCloseDirective,
    IconDirective,
  ],
  templateUrl: './jobs.component.html',
  styleUrl: './jobs.component.scss',
})
export class JobsComponent {
  private hub = inject(HubService);

  readonly jobs           = computed(() => this.hub.jobs());
  readonly selectedJob    = signal<JobContext | null>(null);
  readonly modalVisible   = signal(false);
  readonly selectedTraces = computed(() => {
    const job = this.selectedJob();
    if (!job) return [];
    return this.hub.jobTraces().get(job.jobId) ?? [];
  });

  openTraces(job: JobContext): void {
    this.selectedJob.set(job);
    this.modalVisible.set(true);
  }

  closeModal(): void {
    this.modalVisible.set(false);
    this.selectedJob.set(null);
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
