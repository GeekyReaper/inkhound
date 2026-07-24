import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { OptionsService } from './options.service';

export interface ApiToken {
  id: string;
  name: string;
  prefix: string;
  createdAt: string;
  expiresAt: string | null;
  lastUsedAt: string | null;
}

export interface ApiTokenCreated extends ApiToken {
  token: string;
}

@Injectable({ providedIn: 'root' })
export class ApiTokenService {
  private http = inject(HttpClient);
  private optionsService = inject(OptionsService);

  getAll() {
    return this.http.get<ApiToken[]>('/api/apitokens');
  }

  create(name: string, expiresInDays: number | null) {
    return this.http.post<ApiTokenCreated>('/api/apitokens', { name, expiresInDays });
  }

  delete(id: string) {
    return this.http.delete<void>(`/api/apitokens/${id}`);
  }

  // Lit le booléen 'Enabled' du service 'ApiToken' via le mécanisme générique d'options.
  // Le backend sérialise bool.ToString() en "True"/"False" (PascalCase) — comparaison insensible à la casse.
  isEnabled(): Observable<boolean> {
    return this.optionsService.getOptions('ApiToken').pipe(
      map(options => options.find(o => o.name === 'Enabled')?.value?.toLowerCase() === 'true')
    );
  }
}
