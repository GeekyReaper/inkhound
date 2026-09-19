import { Component, computed, DestroyRef, inject, OnInit, Signal, signal, WritableSignal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import {
  AccordionButtonDirective, AccordionComponent, AccordionItemComponent,
  AlertComponent, BadgeComponent, ButtonDirective, FormCheckComponent,
  FormCheckInputDirective, FormCheckLabelDirective,
  FormControlDirective, FormDirective, FormFeedbackComponent,
  FormLabelDirective, FormSelectDirective, InputGroupComponent, SpinnerComponent, TemplateIdDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { OptionsService } from '../../core/services/options.service';
import { HubService } from '../../core/services/hub.service';
import { EState, EValueType, OptionDefinition } from '../../core/models/hub.models';
import { SelectPathComponent } from '../select-path/select-path.component';

/**
 * État propre à un module dans l'accordéon : plusieurs panneaux pouvant être ouverts
 * simultanément, chaque module porte son propre formulaire, chargement et statut de sauvegarde.
 */
interface ModuleEntry {
  name: string;
  loaded: boolean;
  options: WritableSignal<OptionDefinition[]>;
  form: WritableSignal<FormGroup>;
  loading: WritableSignal<boolean>;
  saving: WritableSignal<boolean>;
  refreshingState: WritableSignal<boolean>;
  saveStatus: WritableSignal<'idle' | 'success' | 'error'>;
  noOptions: WritableSignal<boolean>;
  groupedOptions: Signal<{ section: string; options: OptionDefinition[] }[]>;
  showSectionHeaders: Signal<boolean>;
}

@Component({
  selector: 'app-modules',
  standalone: true,
  imports: [
    AccordionComponent, AccordionItemComponent, AccordionButtonDirective, TemplateIdDirective,
    ReactiveFormsModule,
    FormDirective, FormControlDirective, FormLabelDirective,
    FormCheckComponent, FormCheckInputDirective, FormCheckLabelDirective,
    FormFeedbackComponent, BadgeComponent, ButtonDirective, SpinnerComponent, AlertComponent,
    IconDirective, SelectPathComponent, InputGroupComponent, FormSelectDirective
  ],
  templateUrl: './modules.component.html',
  styleUrl: './modules.component.scss'
})
export class ModulesComponent implements OnInit {
  private optionsService = inject(OptionsService);
  private hubService     = inject(HubService);
  readonly #destroyRef   = inject(DestroyRef);

  entries = signal<ModuleEntry[]>([]);

  pathPickerVisible = signal(false);
  // Module + option ciblés par le sélecteur de chemin (un seul picker partagé pour tous les panneaux)
  activePathTarget  = signal<{ entry: ModuleEntry; option: string } | null>(null);

  ngOnInit() {
    this.optionsService.getServices().subscribe({
      next: names => this.entries.set(names.map(n => this.createEntry(n))),
      error: () => {}
    });
  }

  // Ouverture/fermeture d'un panneau : les options sont chargées à la première ouverture seulement.
  toggle(entry: ModuleEntry, item: AccordionItemComponent): void {
    item.toggleItem();
    if (item.visible && !entry.loaded) this.loadOptions(entry);
  }

  saveOptions(entry: ModuleEntry) {
    const fg = entry.form();
    if (fg.invalid) return;

    const payload: Record<string, string> = {};
    for (const def of entry.options()) {
      const raw = fg.get(def.name)?.value;
      payload[def.name] = def.valueType === 'BOOL' ? String(raw) : String(raw ?? '');
    }

    entry.saving.set(true);
    this.optionsService.updateOptions(entry.name, payload)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  () => { entry.saving.set(false); entry.saveStatus.set('success'); },
        error: () => { entry.saving.set(false); entry.saveStatus.set('error'); }
      });
  }

  // Bypass le cache de StateRefreshDelay côté service (ex: 180 min pour Bedetheque) — utile pour
  // valider immédiatement un changement de config (proxy activé, etc.) sans attendre le prochain
  // cycle de monitoring. Le badge d'état se met à jour tout seul via SignalR (ManagerStateChanged),
  // pas besoin de traiter la réponse HTTP ici.
  refreshState(entry: ModuleEntry): void {
    if (entry.refreshingState()) return;
    entry.refreshingState.set(true);
    this.optionsService.refreshState(entry.name)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  () => entry.refreshingState.set(false),
        error: () => entry.refreshingState.set(false)
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

  openPathPicker(entry: ModuleEntry, optionName: string): void {
    this.activePathTarget.set({ entry, option: optionName });
    this.pathPickerVisible.set(true);
  }

  pathPickerInitialPath(): string {
    const target = this.activePathTarget();
    return target ? (target.entry.form().get(target.option)?.value ?? '') : '';
  }

  onPathSelected(path: string): void {
    const target = this.activePathTarget();
    if (target && path) target.entry.form().get(target.option)?.setValue(path);
    this.activePathTarget.set(null);
  }

  private createEntry(name: string): ModuleEntry {
    const options = signal<OptionDefinition[]>([]);
    const groupedOptions = computed(() => {
      const groups: { section: string; options: OptionDefinition[] }[] = [];
      for (const def of options()) {
        const last = groups.at(-1);
        if (last && last.section === def.section) last.options.push(def);
        else groups.push({ section: def.section, options: [def] });
      }
      return groups;
    });
    const showSectionHeaders = computed(() => new Set(options().map(o => o.section)).size > 1);

    return {
      name,
      loaded: false,
      options,
      form: signal<FormGroup>(new FormGroup({})),
      loading: signal(false),
      saving: signal(false),
      refreshingState: signal(false),
      saveStatus: signal<'idle' | 'success' | 'error'>('idle'),
      noOptions: signal(false),
      groupedOptions,
      showSectionHeaders
    };
  }

  private loadOptions(entry: ModuleEntry): void {
    entry.loaded = true;
    entry.loading.set(true);
    entry.saveStatus.set('idle');
    entry.noOptions.set(false);

    this.optionsService.getOptions(entry.name)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: defs => {
          entry.options.set(defs);
          entry.form.set(this.buildForm(defs));
          entry.loading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          entry.noOptions.set(err.status === 404);
          entry.loading.set(false);
        }
      });
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
