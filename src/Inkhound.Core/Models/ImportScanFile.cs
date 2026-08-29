namespace Inkhound.Core.Models;

// Fichier d'archive trouvé dans un dossier d'import, avec le numéro d'issue déduit de son nom —
// alimente la popup de revue fichiers ↔ issues avant l'import.
public record ImportScanFile(string Name, long Size, int? DetectedIssueNumber);
