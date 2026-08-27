import { Component, computed, DestroyRef, effect, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import {
  AlertComponent, BadgeComponent, ButtonDirective, FormCheckComponent,
  FormCheckInputDirective, FormCheckLabelDirective,
  FormControlDirective, FormDirective, FormFeedbackComponent,
  FormLabelDirective, FormSelectDirective, InputGroupComponent, SpinnerComponent, Tabs2Module
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { OptionsService } from '../../core/services/options.service';
import { HubService } from '../../core/services/hub.service';
import { EState, EValueType, OptionDefinition } from '../../core/models/hub.models';
import { SelectPathComponent } from '../select-path/select-path.component';

@Component({
  selector: 'app-modules',
  standalone: true,
  imports: [
    Tabs2Module, ReactiveFormsModule,
    FormDirective, FormControlDirective, FormLabelDirective,
    FormCheckComponent, FormCheckInputDirective, FormCheckLabelDirective,
    FormFeedbackComponent, BadgeComponent, ButtonDirective, SpinnerComponent, AlertComponent,
    IconDirective, SelectPathComponent, InputGroupComponent, FormSelectDirective
  ],
  templateUrl: './modules.component.html'
})
export class ModulesComponent implements OnInit {
  private optionsService = inject(OptionsService);
  private hubService     = inject(HubService);
  readonly #destroyRef   = inject(DestroyRef);

  services    = signal<string[]>([]);
  activeTab   = signal<string>('');
  activeIndex = computed(() => this.services().indexOf(this.activeTab()));
  options    = signal<OptionDefinition[]>([]);
  form       = signal<FormGroup>(new FormGroup({}));
  loading    = signal(false);
  saving     = signal(false);
  saveStatus = signal<'idle' | 'success' | 'error'>('idle');
  noOptions  = signal(false);

  pathPickerVisible = signal(false);
  activePathOption  = signal<string | null>(null);

  refreshingState = signal(false);

  readonly groupedOptions = computed(() => {
    const groups: { section: string; options: OptionDefinition[] }[] = [];
    for (const def of this.options()) {
      const last = groups.at(-1);
      if (last && last.section === def.section) last.options.push(def);
      else groups.push({ section: def.section, options: [def] });
    }
    return groups;
  });

  readonly showSectionHeaders = computed(() =>
    new Set(this.options().map(o => o.section)).size > 1);

  constructor() {
    effect(() => {
      const name = this.activeTab();
      if (!name) return;

      this.loading.set(true);
      this.saveStatus.set('idle');
      this.noOptions.set(false);

      this.optionsService.getOptions(name)
        .pipe(takeUntilDestroyed(this.#destroyRef))
        .subscribe({
          next: defs => {
            this.options.set(defs);
            this.form.set(this.buildForm(defs));
            this.loading.set(false);
          },
          error: (err: HttpErrorResponse) => {
            this.noOptions.set(err.status === 404);
            this.loading.set(false);
          }
        });
    });
  }

  ngOnInit() {
    this.optionsService.getServices().subscribe({
      next: names => {
        this.services.set(names);
        if (names.length) this.activeTab.set(names[0]);
      },
      error: () => {}
    });
  }

  saveOptions() {
    const name = this.activeTab();
    const fg   = this.form();
    if (fg.invalid || !name) return;

    const payload: Record<string, string> = {};
    for (const def of this.options()) {
      const raw = fg.get(def.name)?.value;
      payload[def.name] = def.valueType === 'BOOL' ? String(raw) : String(raw ?? '');
    }

    this.saving.set(true);
    this.optionsService.updateOptions(name, payload)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  () => { this.saving.set(false); this.saveStatus.set('success'); },
        error: () => { this.saving.set(false); this.saveStatus.set('error'); }
      });
  }

  // Bypass le cache de StateRefreshDelay côté service (ex: 180 min pour Bedetheque) — utile pour
  // valider immédiatement un changement de config (proxy activé, etc.) sans attendre le prochain
  // cycle de monitoring. Le badge d'état se met à jour tout seul via SignalR (ManagerStateChanged),
  // pas besoin de traiter la réponse HTTP ici.
  refreshState(): void {
    const name = this.activeTab();
    if (!name || this.refreshingState()) return;
    this.refreshingState.set(true);
    this.optionsService.refreshState(name)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  () => this.refreshingState.set(false),
        error: () => this.refreshingState.set(false)
      });
  }

  getServiceState(name: string): EState {
    const s = this.hubService.managerState()?.stateServices.find(s => s.serviceName === name);
    return s?.state ?? 'NOTINIT';
  }

  getBadgeColor(state: EState): string {
    switch (state) {
      case 'OK':      return 'success';
      case 'WARNING': return 'warning';
      case 'ERROR':   return 'danger';
      case 'INVALID': return 'warning';
      default:        return 'secondary';
    }
  }

  getInputType(vt: EValueType): string {
    if (vt === 'INT' || vt === 'DOUBLE') return 'number';
    if (vt === 'PASSWORD') return 'password';
    return 'text';
  }

  openPathPicker(optionName: string): void {
    this.activePathOption.set(optionName);
    this.pathPickerVisible.set(true);
  }

  onPathSelected(path: string): void {
    const optionName = this.activePathOption();
    if (optionName && path) this.form().get(optionName)?.setValue(path);
    this.activePathOption.set(null);
  }

  private buildForm(defs: OptionDefinition[]): FormGroup {
    const controls: Record<string, FormControl> = {};
    for (const def of defs) {
      const validators = [];
      if (def.mandatory)      validators.push(Validators.required);
      if (def.regexValidator) validators.push(Validators.pattern(def.regexValidator));

      const init = def.valueType === 'BOOL'
        ? (def.value === 'true')
        : (def.value ?? def.defaultValue ?? '');

      controls[def.name] = new FormControl(init, validators);
    }
    return new FormGroup(controls);
  }
}
