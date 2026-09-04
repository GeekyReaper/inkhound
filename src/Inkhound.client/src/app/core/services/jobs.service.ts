import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { JobContext } from '../models/hub.models';

@Injectable({ providedIn: 'root' })
export class JobsService {
  private http = inject(HttpClient);

  // GET /api/jobs/{jobId} — état courant d'un job (voir JobsController côté backend). Utilisé par
  // HubService pour rattraper un ManagerJobChanged manqué pendant une déconnexion SignalR. Renvoie
  // la même forme que l'event SignalR, pour un merge identique côté appelant. 404 si le job n'a
  // jamais existé ou si sa fenêtre de rétention serveur est dépassée.
  getStatus(jobId: string) {
    return this.http.get<JobContext>(`/api/jobs/${jobId}`);
  }
}
