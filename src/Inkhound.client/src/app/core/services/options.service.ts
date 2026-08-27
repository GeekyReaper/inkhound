import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { OptionDefinition, StateService as ServiceState } from '../models/hub.models';

@Injectable({ providedIn: 'root' })
export class OptionsService {
  private http = inject(HttpClient);

  getServices() {
    return this.http.get<string[]>('/api/options');
  }

  getOptions(serviceName: string) {
    return this.http.get<OptionDefinition[]>(`/api/options/${serviceName}`);
  }

  updateOptions(serviceName: string, updates: Record<string, string>) {
    return this.http.put(`/api/options/${serviceName}`, updates);
  }

  // Force un recalcul immédiat de l'état du service (bypass le cache StateRefreshDelay) — utile
  // après un changement de config (ex: proxy) sans attendre le prochain cycle de monitoring.
  refreshState(serviceName: string) {
    return this.http.post<ServiceState>(`/api/options/${serviceName}/refresh-state`, {});
  }
}
