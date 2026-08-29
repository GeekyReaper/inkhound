// Taille de fichier lisible : "—" si nul/inconnu, sinon MB (0 décimale) ou GB (1 décimale) au-delà
// de 1000 MB. Partagé par les tableaux de fichiers (résultats Prowlarr, matcher fichiers ↔ issues).
export function formatSize(bytes: number | null | undefined): string {
  if (bytes == null || bytes <= 0) return '—';
  const mb = bytes / 1_048_576;
  return mb >= 1000 ? `${(mb / 1024).toFixed(1)} GB` : `${mb.toFixed(0)} MB`;
}
