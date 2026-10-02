using Inkhound.Core.Export;

namespace Inkhound.Core.Models;

/// <summary>
/// Fichier d'export persisté dans le dossier Export : un PDF/CBZ pour une issue, un ZIP pour un
/// volume. Une seule entrée par (cible, format) — une nouvelle demande remplace la précédente. La
/// suppression à l'expiration est assurée par la tâche « Clean export » du planificateur, qui se
/// base sur <see cref="CreatedAt"/>.
/// </summary>
public class ExportFile
{
    public Guid Id { get; set; }

    public ExportTargetType TargetType { get; set; }

    /// <summary>Id de l'issue ou du volume exporté.</summary>
    public Guid TargetId { get; set; }

    /// <summary>Format des issues (pour un volume : format des fichiers contenus dans le ZIP).</summary>
    public ExportFormat Format { get; set; }

    /// <summary>Nom proposé au téléchargement (le fichier disque est nommé d'après <see cref="Id"/>).</summary>
    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTime CreatedAt { get; set; }

    public ExportStatus Status { get; set; }

    /// <summary>Job qui produit (ou a produit) le fichier — permet à l'UI de suivre la génération.</summary>
    public Guid? JobId { get; set; }
}
