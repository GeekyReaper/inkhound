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
  PageItemComponent, PageLinkDirective, PaginationComponent,
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
    PaginationComponent, PageItemComponent, PageLinkDirective,
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

  // Purement front — hub.jobs() est déjà en mémoire (alimenté par SignalR), pas d'appel backend
  // ici. Choix exclusif — Active par défaut : INITIALIZING/RUNNING ; Done : SUCCESS/ERROR.
  filterMode = signal<'active' | 'done'>('active');

  private static isDone(job: JobContext): boolean {
    return job.state === 'SUCCESS' || job.state === 'ERROR';
  }

  readonly filteredJobs = computed(() => {
    const done = this.filterMode() === 'done';
    return this.jobs().filter(j => JobsComponent.isDone(j) === done);
  });

  readonly pageSize   = 20;
  currentPage         = signal(1);

  readonly totalPages = computed(() => Math.ceil(this.filteredJobs().length / this.pageSize));

  readonly pagedJobs = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize;
    return this.filteredJobs().slice(start, start + this.pageSize);
  });

  readonly visiblePages = computed(() => {
    const total   = this.totalPages();
    const current = this.currentPage();
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
    const start = Math.max(1, Math.min(current - 3, total - 6));
    const end   = Math.min(total, start + 6);
    return Array.from({ length: end - start + 1 }, (_, i) => start + i);
  });

  setFilterMode(mode: 'active' | 'done'): void {
    if (this.filterMode() === mode) return;
    this.filterMode.set(mode);
    this.currentPage.set(1);
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.currentPage.set(page);
  }

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
