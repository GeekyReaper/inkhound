import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs/operators';
import {
  AlertComponent, BadgeComponent, ButtonDirective,
  CardBodyComponent, CardComponent, CardHeaderComponent,
  ColComponent, ContainerComponent, RowComponent,
  FormControlDirective, FormDirective, FormFeedbackComponent,
  FormLabelDirective, ProgressComponent, SpinnerComponent, TableDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { OptionsService } from '../../core/services/options.service';
import { HubService } from '../../core/services/hub.service';
import { ProxyInfo, ProxyService, WebshareStatistics } from '../../core/services/proxy.service';
import { EState, OptionDefinition } from '../../core/models/hub.models';

@Component({
  selector: 'app-proxy-settings',
  standalone: true,
  imports: [
    ReactiveFormsModule, FormDirective, FormControlDirective, FormLabelDirective, FormFeedbackComponent,
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardHeaderComponent, CardBodyComponent, BadgeComponent,
    ButtonDirective, SpinnerComponent, AlertComponent, TableDirective, ProgressComponent, IconDirective
  ],
  templateUrl: './proxy.component.html'
})
export class ProxySettingsComponent implements OnInit {
  private optionsService = inject(OptionsService);
  private proxyService = inject(ProxyService);
  private hubService = inject(HubService);
  private readonly destroyRef = inject(DestroyRef);

  readonly serviceName = 'WebshareProxy';

  connectionOptions = signal<OptionDefinition[]>([]);
  form = signal<FormGroup>(new FormGroup({}));
  loading = signal(true);
  saving = signal(false);
  saveStatus = signal<'idle' | 'success' | 'error'>('idle');

  proxies = signal<ProxyInfo[]>([]);
  proxiesLoading = signal(true);
  rotating = signal(false);

  statistics = signal<WebshareStatistics | null>(null);
  statisticsLoading = signal(true);

  servicesUsingProxy = signal<string[]>([]);
  servicesLoading = signal(true);

  ngOnInit(): void {
    this.optionsService.getOptions(this.serviceName)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: defs => {
          this.connectionOptions.set(defs);
          this.form.set(this.buildForm(defs));
        }
      });

    this.loadProxies();
    this.loadStatistics();
    this.loadServicesUsingProxy();
  }

  loadProxies(): void {
    this.proxiesLoading.set(true);
    this.proxyService.getProxies()
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.proxiesLoading.set(false)))
      .subscribe({
        next: proxies => this.proxies.set(proxies),
        error: () => this.proxies.set([])
      });
  }

  loadStatistics(): void {
    this.statisticsLoading.set(true);
    this.proxyService.getStatistics()
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.statisticsLoading.set(false)))
      .subscribe({
        next: stats => this.statistics.set(stats),
        error: () => this.statistics.set(null)
      });
  }

  loadServicesUsingProxy(): void {
    this.servicesLoading.set(true);
    this.proxyService.getServicesUsingProxy()
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.servicesLoading.set(false)))
      .subscribe({
        next: services => this.servicesUsingProxy.set(services),
        error: () => this.servicesUsingProxy.set([])
      });
  }

  rotate(): void {
    this.rotating.set(true);
    this.proxyService.nextProxy()
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.rotating.set(false)))
      .subscribe({
        next: () => this.loadProxies(),
        error: () => this.loadProxies()
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
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false)))
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

  formatGb(value: number): string {
    return value.toFixed(2);
  }

  formatSeconds(value: number): string {
    return value.toFixed(2);
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
