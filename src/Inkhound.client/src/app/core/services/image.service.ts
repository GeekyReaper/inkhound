import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

@Injectable({ providedIn: 'root' })
export class ImageService {
  private http = inject(HttpClient);

  upload(file: File) {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<{ url: string }>('/api/images', form);
  }
}
