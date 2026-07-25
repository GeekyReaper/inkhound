import { Component, computed, inject, input, signal } from '@angular/core';
import { ButtonDirective, ProgressBarComponent, ProgressComponent } from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { HubService } from '../../core/services/hub.service';
import { JobConsoleModalComponent } from '../job-console-modal/job-console-modal.component';

// Composant réutilisable pour toute page qui lance un Job : titre, barre de progression, et
// accès à la console de traces (JobConsoleModalComponent, réutilisé tel quel — le filtrage des
// traces par jobId y est déjà géré). La page appelante reste responsable de déclencher le job et
// de résoudre son jobId (ex: via PageJobService) ; ce composant se contente de l'afficher.
@Component({
  selector: 'app-job-panel',
  standalone: true,
  imports: [ProgressComponent, ProgressBarComponent, ButtonDirective, IconDirective, JobConsoleModalComponent],
  templateUrl: './job-panel.component.html'
})
export class JobPanelComponent {
  private hub = inject(HubService);

  readonly jobId = input<string | null>(null);

  readonly job = computed(() => {
    const id = this.jobId();
    return id ? this.hub.jobs().find(j => j.jobId === id) ?? null : null;
  });

  readonly consoleVisible = signal(false);
}
