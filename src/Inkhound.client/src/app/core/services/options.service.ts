import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { OptionDefinition } from '../models/hub.models';

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
}
