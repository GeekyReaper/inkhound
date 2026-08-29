using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

public class ImportDirectoryJobParameters : IJobParameters
{
    public Guid VolumeId { get; set; }
    public string Directory { get; set; } = string.Empty;

    // Appariement explicite nom de fichier → IssueId, issu de la revue dans la popup. Quand fourni,
    // seuls les fichiers listés sont importés (même vers une issue déjà DOWNLOADED = ré-import).
    // Quand null, appariement automatique par numéro de tome (SourceAnalyzer.ParseIssueNumber) et
    // les issues déjà DOWNLOADED sont ignorées sauf OverrideExisting.
    public Dictionary<string, Guid>? FileIssueMap { get; set; }
    public bool OverrideExisting { get; set; }

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (VolumeId == Guid.Empty)
            errors.Add("VolumeId cannot be empty.");
        if (string.IsNullOrWhiteSpace(Directory))
            errors.Add("Directory cannot be empty.");
        return errors.Count == 0;
    }
}
