import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface DirectoryDto {
  name: string;
  fullPath: string;
  parent: string | null;
  createdAt: string;
  modifiedAt: string;
}

export interface FileDto {
  name: string;
  fullPath: string;
  extension: string;
  sizeBytes: number;
  createdAt: string;
  modifiedAt: string;
}

@Injectable({ providedIn: 'root' })
export class FilesystemService {
  private http = inject(HttpClient);

  getDirectories(path: string) {
    return this.http.get<DirectoryDto[]>('/api/filesystem/directories', { params: { path } });
  }

  getFiles(path: string) {
    return this.http.get<FileDto[]>('/api/filesystem/files', { params: { path } });
  }
}
