import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

@Injectable({ providedIn: 'root' })
export class VersionService {
  private http = inject(HttpClient);

  getVersion() {
    return this.http.get<{ version: string }>('/api/version');
  }
}
