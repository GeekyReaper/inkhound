import { DownloadItem, DownloadStatus } from '../services/qbittorrent.service';

// Formatage et code couleur d'une ligne de téléchargement — partagés par la page Downloads, la
// page Issue (via app-download-list), la page Volume et le Dashboard, qui en avaient chacun leur
// propre copie (avec des divergences : NotFound, gestion des To).

/** Couleur du badge CoreUI correspondant au statut. */
export function downloadStatusColor(status: DownloadStatus): string {
  switch (status) {
    case 'Downloading': return 'info';
    case 'Stalled':     return 'warning';
    case 'Paused':      return 'warning';
    case 'Finished':    return 'success';
    case 'Syncing':     return 'info';
    case 'Done':        return 'success';
    case 'Error':       return 'danger';
    case 'NotFound':    return 'dark';
    default:            return 'secondary';
  }
}

/** Vitesse de téléchargement ; `—` si inconnue ou nulle. */
export function formatSpeed(bytesPerSec: number | null): string {
  if (bytesPerSec === null || bytesPerSec <= 0) return '—';
  if (bytesPerSec >= 1_048_576) return `${(bytesPerSec / 1_048_576).toFixed(1)} MB/s`;
  return `${(bytesPerSec / 1024).toFixed(0)} KB/s`;
}

/** Temps restant ; `—` si inconnu ou « infini » (valeur sentinelle de qBittorrent). */
export function formatEta(seconds: number | null): string {
  if (seconds === null || seconds <= 0 || seconds >= 8640000) return '—';
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = seconds % 60;
  if (h > 0) return `${h}h ${m}m`;
  if (m > 0) return `${m}m ${s}s`;
  return `${s}s`;
}

/** Taille lisible (Mo / Go / To) ; `—` si inconnue. */
export function formatSize(bytes: number | null): string {
  if (bytes === null || bytes <= 0) return '—';
  const mb = bytes / 1_048_576;
  if (mb < 1000) return `${mb.toFixed(0)} MB`;
  const gb = mb / 1024;
  return gb < 1000 ? `${gb.toFixed(1)} GB` : `${(gb / 1024).toFixed(1)} TB`;
}

/** Date d'ajout compacte : `HH:mm` si c'est aujourd'hui, sinon `dd/MM HH:mm`. */
export function formatAdded(value: string | null): string {
  if (!value) return '—';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return '—';
  const pad = (n: number) => n.toString().padStart(2, '0');
  const time = `${pad(d.getHours())}:${pad(d.getMinutes())}`;
  const today = new Date();
  const sameDay = d.getDate() === today.getDate()
    && d.getMonth() === today.getMonth()
    && d.getFullYear() === today.getFullYear();
  return sameDay ? time : `${pad(d.getDate())}/${pad(d.getMonth() + 1)} ${time}`;
}

/** Le téléchargement est terminé côté torrent et peut être importé dans la librairie. */
export function canProcess(item: DownloadItem): boolean {
  return item.status === 'Finished' || item.status === 'Syncing';
}
