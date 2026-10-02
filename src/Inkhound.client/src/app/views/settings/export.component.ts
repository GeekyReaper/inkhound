import { Component, DestroyRef, computed, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs/operators';
import {
  AlertComponent, BadgeComponent, ButtonDirective,
  CardBodyComponent, CardComponent, CardHeaderComponent,
  ColComponent, ContainerComponent, RowComponent,
  FormControlDirective, FormDirective, FormFeedbackComponent,
  FormLabelDirective, FormSelectDirective, SpinnerComponent
} from '@coreui/angular';
import { OptionsService } from '../../core/services/options.service';
import { HubService } from '../../core/services/hub.service';
import { EState, OptionDefinition } from '../../core/models/hub.models';

interface OptionSection {
  name: string;
  options: OptionDefinition[];
}

// Réglages du module Export : dossier de stockage, format par défaut et qualité par format
// (PDF / CBZ). Formulaire généré depuis les définitions d'option du backend, groupées par section.
// La durée de vie des fichiers se règle dans Settings > Scheduler (tâche « Clean export »).
@Component({
  selector: 'app-export-settings',
  standalone: true,
  imports: [
    ReactiveFormsModule, RouterLink, FormDirective, FormControlDirective, FormLabelDirective,
    FormSelectDirective, FormFeedbackComponent,
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardHeaderComponent, CardBodyComponent, BadgeComponent,
    ButtonDirective, SpinnerComponent, AlertComponent
  ],
  templateUrl: './export.component.html'
})
export class ExportSettingsComponent implements OnInit {
  private optionsService = inject(OptionsService);
  private hubService = inject(HubService);
  readonly #destroyRef = inject(DestroyRef);

  readonly serviceName = 'Export';

  options = signal<OptionDefinition[]>([]);
  form = signal<FormGroup>(new FormGroup({}));
  loading = signal(true);
  saving = signal(false);
  saveStatus = signal<'idle' | 'success' | 'error'>('idle');

  sections = computed<OptionSection[]>(() => {
    const result: OptionSection[] = [];
    for (const def of [...this.options()].sort((a, b) => a.sortOrder - b.sortOrder)) {
      let section = result.find(s => s.name === def.section);
      if (!section) {
        section = { name: def.section, options: [] };
        result.push(section);
      }
      section.options.push(def);
    }
    return result;
  });

  ngOnInit(): void {
    this.optionsService.getOptions(this.serviceName)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: defs => {
          this.options.set(defs);
          this.form.set(this.buildForm(defs));
        }
      });
  }

  save(): void {
    const fg = this.form();
    if (fg.invalid) return;

    const payload: Record<string, string> = {};
    for (const def of this.options()) {
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
