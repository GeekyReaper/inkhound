import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface VisionProviderInfo {
  name: string;
  isActive: boolean;   // une clé API est configurée pour ce provider
  model: string;
}

export interface VisionUsageSnapshot {
  requestsLastMinute: number;
  tokensLastMinute: number;
  requestsLastDay: number;
  tokensLastDay: number;
  requestsPerMinuteRemaining: number | null;
  tokensPerMinuteRemaining: number | null;
  requestsPerDayRemaining: number | null;
  tokensPerDayRemaining: number | null;
}

/**
 * État d'exécution du module Chatbot. Entièrement en mémoire côté serveur : les compteurs
 * repartent de zéro à chaque redémarrage, rien n'est persisté.
 */
export interface ChatbotStatus {
  enabled: boolean;              // valeur de l'option, indépendante de l'exécution réelle
  running: boolean;              // la boucle de synchronisation Matrix tourne
  serviceName: string;
  botUserId: string | null;      // renseigné après /whoami
  roomId: string | null;
  homeServerUrl: string | null;
  lastSyncUtc: string | null;
  startedUtc: string | null;
  handledEventCount: number;
  encryptedEventCount: number;   // événements chiffrés reçus : le bot ne sait pas les lire
  pendingMenuCount: number;
  visionProvider: string | null;
  visionConfigured: boolean;
  visionProviders: VisionProviderInfo[];
  visionUsage: VisionUsageSnapshot | null;
  visionCacheEntries: number;
  commands: string[];
}

@Injectable({ providedIn: 'root' })
export class ChatbotService {
  private http = inject(HttpClient);

  getStatus() {
    return this.http.get<ChatbotStatus>('/api/chatbot/status');
  }

  // Démarrage/arrêt ponctuels : ne modifient pas l'option Enabled, qu'une sauvegarde des options
  // du module réappliquera.
  start() {
    return this.http.post<ChatbotStatus>('/api/chatbot/start', {});
  }

  stop() {
    return this.http.post<ChatbotStatus>('/api/chatbot/stop', {});
  }
}
