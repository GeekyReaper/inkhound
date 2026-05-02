import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface KavitaLibrary {
  id: number;
  name: string;
  type: number;
  lastScanned: string;
}

@Injectable({ providedIn: 'root' })
export class KavitaService {
  private http = inject(HttpClient);

  readonly libraries = signal<KavitaLibrary[]>([]);
  readonly loading = signal(false);

  private _loaded = false;

  async loadLibraries(refresh = false): Promise<void> {
    if (this._loaded && !refresh) return;

    this.loading.set(true);
    try {
      const libs = await firstValueFrom(
        this.http.get<KavitaLibrary[]>('/api/kavita/libraries', {
          params: refresh ? { refresh: 'true' } : {}
        })
      );
      this.libraries.set(libs);
      this._loaded = true;
    } finally {
      this.loading.set(false);
    }
  }

  scanLibrary(id: number, force = false) {
    return this.http.post<void>(`/api/kavita/libraries/${id}/scan`, null, {
      params: force ? { force: 'true' } : {}
    });
  }
}
