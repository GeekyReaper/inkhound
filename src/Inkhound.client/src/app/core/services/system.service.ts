import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

// Un cache mémoire applicatif et son nombre d'entrées courant.
export interface CacheInfo {
  name: string;
  entryCount: number;
}

// Instantané de l'empreinte mémoire du process backend.
export interface MemorySnapshot {
  // Mémoire résidente vue par l'OS — c'est la valeur que remonte `docker stats`. Elle inclut la
  // mémoire native des décodeurs d'image, invisible du GC.
  workingSetBytes: number;
  managedHeapBytes: number;
  gcHeapSizeBytes: number;
  // Part des segments du GC libre mais non rendue à l'OS.
  fragmentedBytes: number;
  // Sous Docker, la limite du conteneur (mem_limit). Égale à la RAM de la machine = limite absente.
  totalAvailableMemoryBytes: number;
  // true = un tas par cœur logique. Inkhound est censé tourner en Workstation GC.
  isServerGc: boolean;
  isConcurrentGc: boolean;
  gen0CollectionCount: number;
  gen1CollectionCount: number;
  gen2CollectionCount: number;
  caches: CacheInfo[];
}

export interface MemoryCompactionResult {
  before: MemorySnapshot;
  after: MemorySnapshot;
  cacheEntriesRemoved: number;
  // Différence de working set. Peut être nulle ou négative : la compaction ne récupère que le
  // managé, jamais la mémoire native déjà rendue par les décodeurs.
  bytesFreed: number;
  durationSeconds: number;
}

@Injectable({ providedIn: 'root' })
export class SystemService {
  private http = inject(HttpClient);

  getMemory() {
    return this.http.get<MemorySnapshot>('/api/system/memory');
  }

  // Bloquant côté serveur de l'ordre de la seconde.
  compactMemory() {
    return this.http.post<MemoryCompactionResult>('/api/system/memory/compact', {});
  }
}
