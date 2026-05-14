import { Component, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { switchMap } from 'rxjs';
import {
  AlertComponent,
  CardBodyComponent,
  CardComponent,
  ColComponent,
  ContainerComponent,
  RowComponent,
  SpinnerComponent
} from '@coreui/angular';
import { Volume, VolumeService, VolumeStatus } from '../../core/services/volume.service';
import { Issue, IssueService, IssueStatus } from '../../core/services/issue.service';

@Component({
  selector: 'app-volume',
  templateUrl: './volume.component.html',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent
  ]
})
export class VolumeComponent {
  private route         = inject(ActivatedRoute);
  private volumeService = inject(VolumeService);
  private issueService  = inject(IssueService);
  readonly #destroyRef  = inject(DestroyRef);

  volume        = signal<Volume | null>(null);
  loading       = signal(true);
  error         = signal<string | null>(null);
  issues        = signal<Issue[]>([]);
  issuesLoading = signal(false);

  constructor() {
    this.route.params
      .pipe(
        switchMap(params => this.volumeService.getById(params['volumeId'])),
        takeUntilDestroyed(this.#destroyRef)
      )
      .subscribe({
        next: volume => {
          this.volume.set(volume);
          this.loading.set(false);
          this.loadIssues(volume.id);
        },
        error: err => {
          this.error.set(err?.error?.message ?? 'Volume not found.');
          this.loading.set(false);
        }
      });
  }

  private loadIssues(volumeId: string): void {
    this.issuesLoading.set(true);
    this.issueService.getByVolume(volumeId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  issues => { this.issues.set(issues); this.issuesLoading.set(false); },
        error: ()     => { this.issuesLoading.set(false); }
      });
  }

  volumeStatusBadgeClass(status: VolumeStatus): string {
    const map: Record<VolumeStatus, string> = {
      MONITORED: 'badge bg-primary',
      COMPLETED: 'badge bg-success',
      FREEZE:    'badge bg-secondary'
    };
    return map[status];
  }

  issueStatusBadgeClass(status: IssueStatus): string {
    const map: Record<IssueStatus, string> = {
      SEEKING:     'badge bg-warning text-dark',
      DOWNLOADING: 'badge bg-info text-dark',
      DOWNLOADED:  'badge bg-success',
      MISSING:     'badge bg-danger'
    };
    return map[status];
  }
}
