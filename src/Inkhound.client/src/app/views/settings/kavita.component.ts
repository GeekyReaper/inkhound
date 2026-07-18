import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs/operators';
import {
  AlertComponent, BadgeComponent, ButtonDirective,
  CardBodyComponent, CardComponent, CardHeaderComponent,
  ColComponent, ContainerComponent, RowComponent,
  FormControlDirective, FormDirective, FormFeedbackComponent,
  FormLabelDirective, SpinnerComponent
} from '@coreui/angular';
import { OptionsService } from '../../core/services/options.service';
import { HubService } from '../../core/services/hub.service';
import { EState, OptionDefinition } from '../../core/models/hub.models';
import { ScoringSettingsComponent } from '../scoring-settings/scoring-settings.component';

const CONNECTION_FIELDS = ['BaseUrl', 'ApiKey', 'TimeoutSeconds', 'PluginName'];

@Component({
  selector: 'app-kavita-settings',
  standalone: true,
  imports: [
    ReactiveFormsModule, FormDirective, FormControlDirective, FormLabelDirective, FormFeedbackComponent,
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardHeaderComponent, CardBodyComponent, BadgeComponent,
    ButtonDirective, SpinnerComponent, AlertComponent, ScoringSettingsComponent
  ],
  templateUrl: './kavita.component.html'
})
export class KavitaSettingsComponent implements OnInit {
  private optionsService = inject(OptionsService);
  private hubService = inject(HubService);
  readonly #destroyRef = inject(DestroyRef);

  readonly serviceName = 'Kavita';

  connectionOptions = signal<OptionDefinition[]>([]);
  form = signal<FormGroup>(new FormGroup({}));
  loading = signal(true);
  saving = signal(false);
  saveStatus = signal<'idle' | 'success' | 'error'>('idle');

  ngOnInit(): void {
    this.optionsService.getOptions(this.serviceName)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: defs => {
          const connectionDefs = defs.filter(d => CONNECTION_FIELDS.includes(d.name));
          this.connectionOptions.set(connectionDefs);
          this.form.set(this.buildForm(connectionDefs));
        }
      });
  }

  saveConnection(): void {
    const fg = this.form();
    if (fg.invalid) return;

    const payload: Record<string, string> = {};
    for (const def of this.connectionOptions()) {
      payload[def.name] = String(fg.get(def.name)?.value ?? '');
    }

    this.saving.set(true);
    this.saveStatus.set('idle');
    this.optionsService.updateOptions(this.serviceName, payload)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => this.saveStatus.set('success'),
        error: () => this.saveStatus.set('error')
      });
  }

  getServiceState(): EState {
    const s = this.hubService.managerState()?.stateServices.find(s => s.serviceName === this.serviceName);
    return s?.state ?? 'NOTINIT';
  }

  getBadgeColor(state: EState): string {
    switch (state) {
      case 'OK': return 'success';
      case 'WARNING': return 'warning';
      case 'ERROR': return 'danger';
      default: return 'secondary';
    }
  }

  private buildForm(defs: OptionDefinition[]): FormGroup {
    const controls: Record<string, FormControl> = {};
    for (const def of defs) {
      const validators = [];
      if (def.mandatory) validators.push(Validators.required);
      if (def.regexValidator) validators.push(Validators.pattern(def.regexValidator));
      controls[def.name] = new FormControl(def.value ?? def.defaultValue ?? '', validators);
    }
    return new FormGroup(controls);
  }
}
