import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { forkJoin } from 'rxjs';
import {
  AlertComponent, BadgeComponent, ButtonDirective,
  CardBodyComponent, CardComponent, CardHeaderComponent,
  ColComponent, ContainerComponent, FormCheckComponent,
  FormCheckInputDirective, FormCheckLabelDirective,
  FormControlDirective, FormDirective, FormFeedbackComponent,
  FormLabelDirective, FormSelectDirective, RowComponent, SpinnerComponent
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { OptionsService } from '../../core/services/options.service';
import { HubService } from '../../core/services/hub.service';
import { QBittorrentCategory, QBittorrentService } from '../../core/services/qbittorrent.service';
import { EState, OptionDefinition } from '../../core/models/hub.models';

@Component({
  selector: 'app-qbittorrent-settings',
  standalone: true,
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardHeaderComponent, CardBodyComponent,
    ReactiveFormsModule, FormDirective, FormControlDirective, FormLabelDirective,
    FormSelectDirective, FormCheckComponent, FormCheckInputDirective, FormCheckLabelDirective,
    FormFeedbackComponent, BadgeComponent, ButtonDirective, SpinnerComponent, AlertComponent,
    IconDirective
  ],
  templateUrl: './qbittorrent.component.html'
})
export class QBittorrentSettingsComponent implements OnInit {
  private optionsService    = inject(OptionsService);
  private qbService         = inject(QBittorrentService);
  private hubService        = inject(HubService);
  readonly #destroyRef      = inject(DestroyRef);

  readonly serviceName = 'QBittorrent';

  options     = signal<OptionDefinition[]>([]);
  categories  = signal<QBittorrentCategory[]>([]);
  form        = signal<FormGroup>(new FormGroup({}));
  loading     = signal(true);
  saving      = signal(false);
  testing     = signal(false);
  saveStatus  = signal<'idle' | 'success' | 'error'>('idle');
  testStatus  = signal<'idle' | 'ok' | 'error'>('idle');

  ngOnInit() {
    forkJoin({
      options: this.optionsService.getOptions(this.serviceName),
      categories: this.qbService.getCategories()
    })
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: ({ options, categories }) => {
          this.options.set(options);
          this.categories.set(categories);
          this.form.set(this.buildForm(options));
          this.loading.set(false);
        },
        error: () => {
          // categories may fail if service not configured yet — load options only
          this.optionsService.getOptions(this.serviceName)
            .pipe(takeUntilDestroyed(this.#destroyRef))
            .subscribe({
              next: options => {
                this.options.set(options);
                this.form.set(this.buildForm(options));
                this.loading.set(false);
              },
              error: () => this.loading.set(false)
            });
        }
      });
  }

  testConnection() {
    this.testing.set(true);
    this.testStatus.set('idle');
    this.qbService.getCategories()
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: cats => {
          this.categories.set(cats);
          this.testStatus.set('ok');
          this.testing.set(false);
        },
        error: () => {
          this.testStatus.set('error');
          this.testing.set(false);
        }
      });
  }

  saveOptions() {
    const fg = this.form();
    if (fg.invalid) return;

    const payload: Record<string, string> = {};
    for (const def of this.options()) {
      const raw = fg.get(def.name)?.value;
      payload[def.name] = def.valueType === 'BOOL' ? String(raw) : String(raw ?? '');
    }

    this.saving.set(true);
    this.saveStatus.set('idle');
    this.optionsService.updateOptions(this.serviceName, payload)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  () => { this.saving.set(false); this.saveStatus.set('success'); },
        error: () => { this.saving.set(false); this.saveStatus.set('error'); }
      });
  }

  getServiceState(): EState {
    const s = this.hubService.managerState()?.stateServices.find(s => s.serviceName === this.serviceName);
    return s?.state ?? 'NOTINIT';
  }

  getBadgeColor(state: EState): string {
    switch (state) {
      case 'OK':      return 'success';
      case 'WARNING': return 'warning';
      case 'ERROR':   return 'danger';
      default:        return 'secondary';
    }
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
